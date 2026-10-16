using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;

namespace DirectPackageInstaller;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>Uma notificação curta exibida no canto da tela.</summary>
public class ToastItem
{
    public ToastItem(string Title, string Message, ToastKind Kind)
    {
        this.Title = Title;
        this.Message = Message ?? "";
        this.Kind = Kind;
    }

    public string Title { get; }
    public string Message { get; }
    public ToastKind Kind { get; }

    public bool HasMessage => Message.Length > 0;

    public IBrush AccentBrush => new SolidColorBrush(Color.Parse(Kind switch
    {
        ToastKind.Success => "#4ADE80",
        ToastKind.Warning => "#FBBF24",
        ToastKind.Error => "#F87171",
        _ => "#60A5FA"
    }));

    /// <summary>Ícone em traço na grade 16x16 (mesmo estilo de Styles/Icons.axaml).</summary>
    public Geometry Icon => Geometry.Parse(Kind switch
    {
        ToastKind.Success => "M3,8.5 L6.5,12 L13,4.5",
        ToastKind.Warning => "M8,2 L14.5,13.5 H1.5 Z M8,6.5 V9.5 M8,11.2 V11.8",
        ToastKind.Error => "M8,1.5 A6.5,6.5 0 1 1 7.99,1.5 Z M5.5,5.5 L10.5,10.5 M10.5,5.5 L5.5,10.5",
        _ => "M8,1.5 A6.5,6.5 0 1 1 7.99,1.5 Z M8,7 V11.5 M8,4.6 V4.7"
    });
}

/// <summary>
/// Notificações não bloqueantes. Use para confirmações e avisos informativos;
/// erros que exigem decisão continuam em diálogo (MessageBox).
/// </summary>
public static class Toast
{
    private const int MaxVisible = 4;

    public static ObservableCollection<ToastItem> Items { get; } = new ObservableCollection<ToastItem>();

    public static void Show(string Title, string Message = "", ToastKind Kind = ToastKind.Info, double Seconds = 5)
    {
        void Add()
        {
            var Item = new ToastItem(Title, Message, Kind);

            Items.Add(Item);

            while (Items.Count > MaxVisible)
                Items.RemoveAt(0);

            DispatcherTimer.RunOnce(() => Dismiss(Item), TimeSpan.FromSeconds(Seconds));
        }

        if (Dispatcher.UIThread.CheckAccess())
            Add();
        else
            Dispatcher.UIThread.Post(Add);
    }

    public static void Dismiss(ToastItem Item) => Items.Remove(Item);
}
