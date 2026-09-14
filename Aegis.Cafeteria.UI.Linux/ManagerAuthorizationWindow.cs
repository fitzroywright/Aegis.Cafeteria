using Aegis.Cafeteria.Application.Security;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Aegis.Cafeteria.UI.Linux;

public sealed class ManagerAuthorizationWindow : Window
{
    private readonly IOperatorAuthenticator authenticator;
    private readonly IManagerAuthorizationRegistry registry;
    private readonly TextBox userName = new() { MinWidth = 300 };
    private readonly TextBox password = new() { MinWidth = 300, PasswordChar = '●' };
    private readonly TextBlock status = new() { Foreground = Avalonia.Media.Brushes.Firebrick };
    private readonly Button authorize = new() { Content = "Authorize", MinWidth = 110 };

    public ManagerAuthorizationWindow(IOperatorAuthenticator authenticator, IManagerAuthorizationRegistry registry, string action)
    {
        this.authenticator = authenticator;
        this.registry = registry;
        Title = "Manager Authorization";
        Width = 480;
        Height = 300;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Avalonia.Thickness(28), Spacing = 8 };
        root.Children.Add(new TextBlock { Text = action, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
        root.Children.Add(new TextBlock { Text = "A cafeteria manager must authorize this action.", Foreground = Avalonia.Media.Brushes.Gray });
        root.Children.Add(new TextBlock { Text = "Username", Margin = new Avalonia.Thickness(0, 8, 0, 0) });
        root.Children.Add(userName);
        root.Children.Add(new TextBlock { Text = "Password" });
        root.Children.Add(password);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Avalonia.Thickness(0, 10, 0, 0) };
        actions.Children.Add(authorize);
        actions.Children.Add(status);
        root.Children.Add(actions);
        Content = root;
        authorize.Click += async (_, _) => await AuthorizeAsync();
        password.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; await AuthorizeAsync(); } };
    }

    private async Task AuthorizeAsync()
    {
        authorize.IsEnabled = false;
        try
        {
            var identity = await authenticator.AuthenticateAsync(userName.Text ?? string.Empty, password.Text ?? string.Empty);
            password.Text = string.Empty;
            if (identity is null) { status.Text = "Sign-in failed."; return; }
            if (!identity.IsManager) { status.Text = "This account is not a cafeteria manager."; return; }
            registry.Grant(identity);
            Close(identity);
        }
        catch (Exception ex) { status.Text = ex.GetBaseException().Message; }
        finally { authorize.IsEnabled = true; }
    }
}
