using Aegis.Cafeteria.Application.Security;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Aegis.Cafeteria.UI.Linux;

public sealed class OperatorLoginWindow : Window
{
    private readonly IOperatorAuthenticator authenticator;
    private readonly IOperatorSession session;
    private readonly TextBox userName = new() { MinWidth = 320 };
    private readonly TextBox password = new() { MinWidth = 320, PasswordChar = '●' };
    private readonly TextBlock status = new() { Foreground = Brushes.Firebrick };
    private readonly Button signIn = new() { Content = "Sign In", MinWidth = 120 };

    public OperatorLoginWindow(IOperatorAuthenticator authenticator, IOperatorSession session)
    {
        this.authenticator = authenticator;
        this.session = session;
        Title = "Cafeteria Sign In";
        Width = 520;
        Height = 360;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        var header = new Border { Background = new SolidColorBrush(Color.FromRgb(24,45,68)), Padding = new Avalonia.Thickness(24,18) };
        var brand = new StackPanel { Spacing = 2 };
        brand.Children.Add(new TextBlock { Text = "CAFETERIA", Foreground = Brushes.White, FontSize = 24, FontWeight = FontWeight.Bold });
        brand.Children.Add(new TextBlock { Text = "Food For The Poor Jamaica", Foreground = new SolidColorBrush(Color.FromRgb(193,210,225)) });
        header.Child = brand;
        root.Children.Add(header);

        var form = new StackPanel { Margin = new Avalonia.Thickness(34), Spacing = 9 };
        form.Children.Add(new TextBlock { Text = "Operator Sign In", FontSize = 20, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.FromRgb(24,45,68)) });
        form.Children.Add(new TextBlock { Text = "Username" }); form.Children.Add(userName);
        form.Children.Add(new TextBlock { Text = "Password" }); form.Children.Add(password);
        form.Children.Add(status); form.Children.Add(signIn);
        Grid.SetRow(form, 1); root.Children.Add(form); Content = root;
        signIn.Click += async (_, _) => await SignInAsync();
        password.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; await SignInAsync(); } };
        Closing += (_, e) => { if (!session.IsAuthenticated) { } };
    }

    private async Task SignInAsync()
    {
        signIn.IsEnabled = false;
        try
        {
            var identity = await authenticator.AuthenticateAsync(userName.Text ?? string.Empty, password.Text ?? string.Empty);
            password.Text = string.Empty;
            if (identity is null) { status.Text = "Sign-in failed."; return; }
            session.SignIn(identity);
            Close(true);
        }
        catch (Exception ex) { status.Text = ex.GetBaseException().Message; }
        finally { signIn.IsEnabled = true; }
    }
}
