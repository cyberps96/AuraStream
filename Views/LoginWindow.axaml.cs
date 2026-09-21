using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using sopfiy.Services;

namespace sopfiy.Views;

public partial class LoginWindow : Window
{
    private static readonly IBrush BrushMint = SolidColorBrush.Parse("#10B981");
    private static readonly IBrush BrushMuted = SolidColorBrush.Parse("#7A9299");

    private CancellationTokenSource? _authCts;

    public bool LoginSuccessful { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        Opened += (s, e) => UpdateCurrentState();
    }

    private void UpdateCurrentState()
    {
        if (AuthManager.IsLoggedIn)
        {
            TxtAccountStatus.Text = $"Connected as {AuthManager.UserDisplayName}";
            TxtAccountStatus.Foreground = BrushMint;
            DotStatusIndicator.Fill = BrushMint;
            BtnSignOut.IsVisible = true;
            BtnSignInGoogle.Content = "Switch Google Account";
        }
        else
        {
            TxtAccountStatus.Text = "Offline (Guest)";
            TxtAccountStatus.Foreground = BrushMuted;
            DotStatusIndicator.Fill = BrushMuted;
            BtnSignOut.IsVisible = false;
            BtnSignInGoogle.Content = "Sign in with Google";
        }
    }

    private async void BtnSignInGoogle_Click(object? sender, RoutedEventArgs e)
    {
        _authCts?.Cancel();
        _authCts?.Dispose();
        _authCts = new CancellationTokenSource();
        var token = _authCts.Token;

        BtnSignInGoogle.IsEnabled = false;
        TxtError.IsVisible = false;
        BorderProgress.IsVisible = true;
        TxtProgress.Text = "Launching secure Google login window...";

        var progress = new Progress<string>(msg =>
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                TxtProgress.Text = msg;
            });
        });

        try
        {
            bool success = await BrowserAuthService.StartInteractiveLoginAsync(progress, token);
            if (success)
            {
                LoginSuccessful = true;
                Close(true);
            }
            else
            {
                BorderProgress.IsVisible = false;
                BtnSignInGoogle.IsEnabled = true;
                ShowError("Sign-in window was closed without completing authentication.");
            }
        }
        catch (OperationCanceledException)
        {
            BorderProgress.IsVisible = false;
            BtnSignInGoogle.IsEnabled = true;
        }
        catch (Exception ex)
        {
            BorderProgress.IsVisible = false;
            BtnSignInGoogle.IsEnabled = true;
            ShowError(ex.Message);
        }
    }

    private async void BtnApplyManualCookie_Click(object? sender, RoutedEventArgs e)
    {
        string rawInput = TxtManualCookie.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            ShowError("Please enter a cookie string or SAPISID token.");
            return;
        }

        try
        {
            Dictionary<string, string> cookies;

            if (rawInput.Contains('='))
            {
                cookies = AuthManager.ParseCookieString(rawInput);
            }
            else
            {
                cookies = new Dictionary<string, string>
                {
                    ["SAPISID"] = rawInput
                };
            }

            if (cookies.Count == 0)
            {
                ShowError("No valid cookies found in input.");
                return;
            }

            await AuthManager.SaveSessionAsync(cookies, "Google Account", string.Empty);
            await AuthManager.RefreshProfileFromSessionAsync();

            LoginSuccessful = true;
            Close(true);
        }
        catch (Exception ex)
        {
            ShowError($"Connection error: {ex.Message}");
        }
    }

    private void BtnSignOut_Click(object? sender, RoutedEventArgs e)
    {
        AuthManager.SignOut();
        UpdateCurrentState();
        TxtError.IsVisible = false;
        BorderProgress.IsVisible = false;
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void ShowError(string message)
    {
        TxtError.Text = message;
        TxtError.IsVisible = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _authCts?.Cancel();
        _authCts?.Dispose();
        base.OnClosing(e);
    }
}
