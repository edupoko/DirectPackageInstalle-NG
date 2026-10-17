using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using DirectPackageInstaller.Views;

namespace DirectPackageInstaller;

/// <summary>
/// Garante uma única cópia do app (modo gráfico) por usuário.
///
/// A trava é um arquivo aberto com acesso exclusivo: o sistema o libera sozinho
/// quando o processo termina, mesmo se for morto à força. Se já houver outra
/// cópia, esta pede a ela (por um pipe nomeado local) que venha para a frente
/// e encerra, em vez de tentar subir de novo e falhar nas portas.
/// </summary>
public static class SingleInstance
{
    private const string ActivateCommand = "ACTIVATE";
    private const string AckReply = "OK";

    private static string Id => $"DirectPackageInstaller-{Environment.UserName}";

    private static string LockPath => Path.Combine(Path.GetTempPath(), Id + ".lock");

    //Mantém a trava até o fim do processo
    private static FileStream? LockHandle;

    /// <summary>
    /// Verdadeiro se esta é a cópia principal e deve continuar iniciando.
    /// Falso se já existe outra aberta (ela foi avisada) e esta deve encerrar.
    /// </summary>
    public static bool TryAcquire()
    {
        try
        {
            LockHandle = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            NotifyRunningInstance();
            return false;
        }
        catch
        {
            //Sem como criar a trava (disco somente leitura etc.): não impede a abertura
            return true;
        }

        StartListener();
        return true;
    }

    private static void NotifyRunningInstance()
    {
        try
        {
            using var Client = new NamedPipeClientStream(".", Id, PipeDirection.InOut);
            Client.Connect(3000);

            var Writer = new StreamWriter(Client) { AutoFlush = true };
            Writer.WriteLine(ActivateCommand);

            var Reply = Task.Run(() => new StreamReader(Client).ReadLine());

            if (Reply.Wait(2500) && Reply.Result == AckReply)
            {
                Console.Error.WriteLine("NeoDPI is already open. The existing window was brought to the front.");
                return;
            }
        }
        catch
        {
        }

        Console.Error.WriteLine("NeoDPI is already running but is not responding. Close it (or run: pkill -9 -f DirectPackageInstaller.Desktop) and try again.");
    }

    private static void StartListener()
    {
        //Temos a trava, então um arquivo de socket antigo no Linux/macOS só pode ser de uma cópia que caiu
        try
        {
            var Stale = Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + Id);

            if (!OperatingSystem.IsWindows() && File.Exists(Stale))
                File.Delete(Stale);
        }
        catch
        {
        }

        var Listener = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var Server = new NamedPipeServerStream(Id, PipeDirection.InOut, 1, PipeTransmissionMode.Byte);
                    Server.WaitForConnection();

                    var Command = new StreamReader(Server).ReadLine();

                    if (Command != ActivateCommand)
                        continue;

                    new StreamWriter(Server) { AutoFlush = true }.WriteLine(AckReply);

                    BringToFront();
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }
        })
        {
            IsBackground = true,
            Name = "SingleInstanceListener"
        };

        Listener.Start();
    }

    /// <summary>Traz a janela principal para a frente e avisa o usuário.</summary>
    public static void BringToFront()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var Window = MainWindow.Instance;

            if (Window == null)
                return;

            if (Window.WindowState == WindowState.Minimized)
                Window.WindowState = WindowState.Normal;

            Window.Show();
            Window.Activate();

            //Alguns gerenciadores de janela só levantam a janela com este truque
            Window.Topmost = true;
            Window.Topmost = false;

            Toast.Show("Already open", "NeoDPI was already running; this window was brought to the front.");
        });
    }
}
