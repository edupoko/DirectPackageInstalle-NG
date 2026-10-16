using System;
using System.Collections.Generic;
using System.Linq;

namespace DirectPackageInstaller.Host
{
    public enum TransferPhase
    {
        Idle,
        /// <summary>Preparando o envio (pré-carga, descompressão, registro no console).</summary>
        Preparing,
        /// <summary>Pedido aceito pelo console; aguardando o console começar a baixar.</summary>
        Waiting,
        /// <summary>O console está baixando o pacote deste PC.</summary>
        Transferring,
        /// <summary>O console baixou tudo o que este PC tinha para entregar.</summary>
        Finished,
        /// <summary>O console baixa direto da origem; o PC não vê o progresso.</summary>
        Direct,
        Failed,
        Cancelled
    }

    /// <summary>Resumo de um envio, usado para o histórico.</summary>
    public sealed class TransferSession
    {
        public DateTime Date { get; init; }
        public string Title { get; init; } = "";
        public string TitleId { get; init; } = "";
        public string Version { get; init; } = "";
        public string Source { get; init; } = "";
        public long TotalBytes { get; init; }
        public TransferPhase Result { get; init; }
    }

    /// <summary>
    /// Acompanha um envio de PKG ao console.
    ///
    /// No GoldHEN o console puxa o pacote do servidor do DPI, então os bytes
    /// entregues por este PC são o progresso do download no console. O que o
    /// console faz depois do download (instalar) não é reportado ao PC.
    /// </summary>
    public sealed class TransferMonitor
    {
        public static TransferMonitor Instance { get; } = new TransferMonitor();

        /// <summary>Segundos sem pedidos do console, na fase de espera, até assumir download direto.</summary>
        private const int DirectModeTimeoutSeconds = 25;

        /// <summary>Segundos sem dados depois de entregar tudo, até considerar o download concluído.</summary>
        private const int FinishIdleSeconds = 6;

        /// <summary>Segundos que o resultado final fica visível na barra de estado.</summary>
        private const int ResultVisibleSeconds = 25;

        private readonly object Sync = new object();
        private readonly Dictionary<string, long> LastSeen = new Dictionary<string, long>();
        private readonly Queue<(DateTime At, long Bytes)> Samples = new Queue<(DateTime, long)>();

        public event Action<TransferSession>? SessionEnded;

        public TransferPhase Phase { get; private set; } = TransferPhase.Idle;
        public string Title { get; private set; } = "";
        public string TitleId { get; private set; } = "";
        public string Version { get; private set; } = "";
        public string Source { get; private set; } = "";
        public long TotalBytes { get; private set; }
        public long ServedBytes { get; private set; }
        public double BytesPerSecond { get; private set; }

        // ---- Tarefa no console (Remote Package Installer) ----
        //
        // Quando o RPI devolve o número da tarefa, o progresso vem do próprio console
        // (bytes, tempo restante e erro), inclusive no modo direto, em que o PC não
        // participa da transferência.

        private const int MaxRemoteMisses = 5;
        private int RemoteMisses;
        private int RemoteRestSeconds;
        private int RemotePreparingPercent;

        public int? ConsoleTaskId { get; private set; }
        public string ConsoleIp { get; private set; } = "";
        public bool ConsolePaused { get; private set; }

        /// <summary>O cancelamento também removeu a tarefa do console.</summary>
        public bool CancelledOnConsole { get; private set; }

        /// <summary>Erro informado pelo console (vazio se não houve).</summary>
        public string FailureText { get; private set; } = "";

        public bool HasConsoleTask => ConsoleTaskId != null;

        /// <summary>Percentual de preparação informado pelo console antes de começar a baixar (-1 se desconhecido).</summary>
        public int PreparingPercent => RemotePreparingPercent;

        private DateTime SentAt = DateTime.MinValue;
        private DateTime LastBytesAt = DateTime.MinValue;
        private DateTime EndedAt = DateTime.MinValue;

        public bool IsActive => Phase is TransferPhase.Preparing or TransferPhase.Waiting or TransferPhase.Transferring;

        public double Progress => TotalBytes > 0 ? Math.Clamp((double)ServedBytes / TotalBytes, 0, 1) : 0;

        public TimeSpan? Eta
        {
            get
            {
                if (Phase == TransferPhase.Transferring && HasConsoleTask && RemoteRestSeconds > 0 && !ConsolePaused)
                    return TimeSpan.FromSeconds(RemoteRestSeconds);

                if (Phase != TransferPhase.Transferring || BytesPerSecond < 1 || TotalBytes <= ServedBytes)
                    return null;

                return TimeSpan.FromSeconds((TotalBytes - ServedBytes) / BytesPerSecond);
            }
        }

        public void Begin(string Title, string TitleId, string Version, string Source, long TotalBytes)
        {
            TransferSession? Previous = null;

            lock (Sync)
            {
                //Um novo envio (ex.: Install All) encerra o anterior que ainda estava aberto
                if (IsActive)
                {
                    var Delivered = this.TotalBytes > 0 && ServedBytes >= this.TotalBytes * 0.995;
                    Previous = Finish(Delivered ? TransferPhase.Finished : TransferPhase.Direct);
                }

                LastSeen.Clear();
                Samples.Clear();

                this.Title = Title;
                this.TitleId = TitleId;
                this.Version = Version;
                this.Source = Source;
                this.TotalBytes = Math.Max(0, TotalBytes);
                ServedBytes = 0;
                BytesPerSecond = 0;
                SentAt = LastBytesAt = EndedAt = DateTime.MinValue;
                ConsoleTaskId = null;
                ConsoleIp = "";
                ConsolePaused = false;
                CancelledOnConsole = false;
                FailureText = "";
                RemoteMisses = 0;
                RemoteRestSeconds = 0;
                RemotePreparingPercent = -1;
                Phase = TransferPhase.Preparing;
            }

            if (Previous != null)
                SessionEnded?.Invoke(Previous);
        }

        /// <summary>O console aceitou o pedido de instalação.</summary>
        public void MarkSent()
        {
            lock (Sync)
            {
                if (Phase != TransferPhase.Preparing)
                    return;

                SentAt = DateTime.Now;
                Phase = ServedBytes > 0 ? TransferPhase.Transferring : TransferPhase.Waiting;
            }
        }

        public void MarkFailed() => End(TransferPhase.Failed);

        public void MarkCancelled(bool OnConsole = false)
        {
            lock (Sync)
                CancelledOnConsole = OnConsole;

            End(TransferPhase.Cancelled);
        }

        /// <summary>Passa a acompanhar a tarefa criada no console pelo RPI.</summary>
        public void AttachConsoleTask(int TaskId, string Ip)
        {
            lock (Sync)
            {
                if (!IsActive)
                    return;

                ConsoleTaskId = TaskId;
                ConsoleIp = Ip;
                RemoteMisses = 0;
                Samples.Clear();
            }
        }

        public void SetPaused(bool Paused)
        {
            lock (Sync)
                ConsolePaused = Paused;
        }

        /// <summary>Aplica o progresso informado pelo console.</summary>
        public void ApplyRemote(RpiProgress Progress)
        {
            TransferSession? Ended = null;

            lock (Sync)
            {
                if (!IsActive || !HasConsoleTask)
                    return;

                RemoteMisses = 0;
                RemoteRestSeconds = Progress.RestSecTotal;
                RemotePreparingPercent = Progress.PreparingPercent;

                if (Progress.Error != 0)
                {
                    FailureText = RpiClient.DescribeError(Progress.Error);
                    Ended = Finish(TransferPhase.Failed);
                }
                else
                {
                    if (Progress.LengthTotal > 0)
                        TotalBytes = Progress.LengthTotal;

                    var Delta = Progress.TransferredTotal - ServedBytes;
                    ServedBytes = Progress.TransferredTotal;

                    if (Delta > 0)
                    {
                        var Now = DateTime.Now;
                        LastBytesAt = Now;
                        Samples.Enqueue((Now, Delta));
                    }

                    if (Progress.IsComplete)
                        Ended = Finish(TransferPhase.Finished);
                    else
                        Phase = Progress.TransferredTotal > 0 ? TransferPhase.Transferring : TransferPhase.Waiting;
                }
            }

            if (Ended != null)
                SessionEnded?.Invoke(Ended);
        }

        /// <summary>
        /// O console não respondeu à consulta da tarefa. Depois de várias falhas seguidas (a tarefa foi
        /// removida no console ou ele desligou), volta a acompanhar só pelo que o PC entrega.
        /// </summary>
        public void RemoteMiss()
        {
            lock (Sync)
            {
                if (!HasConsoleTask || ++RemoteMisses < MaxRemoteMisses)
                    return;

                ConsoleTaskId = null;
                ConsolePaused = false;
                SentAt = DateTime.Now;
            }
        }

        /// <summary>Encerra o painel de resultado.</summary>
        public void Dismiss()
        {
            lock (Sync)
            {
                if (!IsActive)
                    Phase = TransferPhase.Idle;
            }
        }

        /// <summary>Chamado pelo servidor a cada bloco entregue ao console.</summary>
        public void OnProgress(TransferProgressInfo Info)
        {
            lock (Sync)
            {
                if (!IsActive || HasConsoleTask)
                    return;

                //O progresso é por requisição; soma só o que cada uma entregou desde a última leitura
                LastSeen.TryGetValue(Info.RequestPath, out var Previous);
                var Delta = Info.ResponseBytesSent >= Previous ? Info.ResponseBytesSent - Previous : Info.ResponseBytesSent;
                LastSeen[Info.RequestPath] = Info.ResponseBytesSent;

                if (Delta <= 0)
                    return;

                var Now = DateTime.Now;
                ServedBytes += Delta;
                LastBytesAt = Now;
                Samples.Enqueue((Now, Delta));

                if (Phase is TransferPhase.Preparing or TransferPhase.Waiting)
                    Phase = TransferPhase.Transferring;
            }
        }

        /// <summary>Atualiza velocidade e detecta conclusão, modo direto e fim da exibição. Chamar ~1x por segundo.</summary>
        public void Tick()
        {
            TransferSession? Ended = null;

            lock (Sync)
            {
                var Now = DateTime.Now;

                while (Samples.Count > 0 && (Now - Samples.Peek().At).TotalSeconds > 4)
                    Samples.Dequeue();

                BytesPerSecond = Samples.Count == 0 ? 0 : Samples.Sum(x => x.Bytes) / 4.0;

                if (HasConsoleTask)
                {
                    //O fim vem do progresso do console (ApplyRemote); aqui só se limpa o resultado antigo
                    if (!IsActive && Phase != TransferPhase.Idle && EndedAt != DateTime.MinValue
                        && (Now - EndedAt).TotalSeconds >= ResultVisibleSeconds)
                        Phase = TransferPhase.Idle;
                }
                else if (Phase == TransferPhase.Waiting && ServedBytes == 0 && SentAt != DateTime.MinValue
                    && (Now - SentAt).TotalSeconds >= DirectModeTimeoutSeconds)
                {
                    Ended = Finish(TransferPhase.Direct);
                }
                else if (Phase == TransferPhase.Transferring && TotalBytes > 0
                    && ServedBytes >= TotalBytes * 0.995
                    && (Now - LastBytesAt).TotalSeconds >= FinishIdleSeconds)
                {
                    Ended = Finish(TransferPhase.Finished);
                }
                else if (!IsActive && Phase != TransferPhase.Idle && EndedAt != DateTime.MinValue
                    && (Now - EndedAt).TotalSeconds >= ResultVisibleSeconds)
                {
                    Phase = TransferPhase.Idle;
                }
            }

            if (Ended != null)
                SessionEnded?.Invoke(Ended);
        }

        private void End(TransferPhase Result)
        {
            TransferSession? Ended;

            lock (Sync)
            {
                if (!IsActive)
                    return;

                Ended = Finish(Result);
            }

            SessionEnded?.Invoke(Ended);
        }

        private TransferSession Finish(TransferPhase Result)
        {
            Phase = Result;
            EndedAt = DateTime.Now;
            BytesPerSecond = 0;

            return new TransferSession
            {
                Date = EndedAt,
                Title = Title,
                TitleId = TitleId,
                Version = Version,
                Source = Source,
                TotalBytes = TotalBytes,
                Result = Result
            };
        }
    }
}
