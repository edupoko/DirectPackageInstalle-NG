using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using DirectPackageInstaller.ViewModels;
using DirectPackageInstaller.Views;
using Path = Avalonia.Controls.Shapes.Path;

namespace DirectPackageInstaller.UIBase;

/// <summary>
/// Base das caixas de diálogo (MessageBox, Select, LinkList, CookieManager).
///
/// Remove a moldura do sistema (o KDE desenhava a dele por cima) e envolve o
/// conteúdo com borda fina e barra de título própria, no mesmo estilo da
/// janela principal: título, arrastar para mover, fechar (X) e Esc.
/// </summary>
public abstract class DialogWindow : Window
{
    private const string DefaultTitle = "DirectPackageInstaller";

    private TextBlock? TitleText;

    //A janela ainda não está na tela durante a inicialização: se a busca por ela
    //não alcançar os recursos do app, procura direto no Application.
    private object? Resource(string Key) => this.FindResource(Key) ?? Application.Current?.FindResource(Key);

    protected override void OnInitialized()
    {
        base.OnInitialized();
        ApplyChrome();
    }

    private void ApplyChrome()
    {
        SystemDecorations = SystemDecorations.None;

        if (Resource("AppBackgroundBrush") is IBrush Background)
            this.Background = Background;

        var BorderBrush = Resource("BorderStrongBrush") as IBrush;

        var Inner = Content;
        Content = null;

        TitleText = new TextBlock
        {
            Classes = { "caption" },
            Foreground = Brushes.White,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = string.IsNullOrWhiteSpace(Title) ? DefaultTitle : Title
        };

        var CloseButton = new Button
        {
            Classes = { "window", "wincontrol", "close" },
            Content = new Path { Classes = { "icon" }, Data = Resource("IconClose") as Geometry }
        };
        ToolTip.SetTip(CloseButton, "Close");
        CloseButton.Click += (_, _) => Close();
        Grid.SetColumn(CloseButton, 1);

        var TitleBar = new Border
        {
            Height = 36,
            Background = Brushes.Transparent,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children = { TitleText, CloseButton }
            }
        };
        TitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };

        var Body = new ContentControl { Content = Inner };
        Grid.SetRow(Body, 1);

        Content = new Border
        {
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                Children = { TitleBar, Body }
            }
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleProperty && TitleText != null)
            TitleText.Text = string.IsNullOrWhiteSpace(Title) ? DefaultTitle : Title;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!e.Handled && e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    public static T CreateInstance<T>() where T : Window, new()
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.InvokeAsync(CreateInstance<T>).Result;

        return new T();
    }

    public DialogResult ShowDialogSync(Window? Parent = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.InvokeAsync(() => ShowDialogSync(Parent)).Result;

        Extensions.ShowDialogSync(this, Parent ?? MainWindow.Instance);

        return ((DialogModel)DataContext).Result;
    }

    public async Task<DialogResult> ShowDialogAsync(Window? Parent = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(async () => await ShowDialogAsync(Parent));

        await ShowDialog(Parent ?? MainWindow.Instance);

        return ((DialogModel)DataContext).Result;
    }
}
