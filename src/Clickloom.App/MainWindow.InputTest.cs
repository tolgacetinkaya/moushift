using System.Windows;
using Clickloom.Core;
using Clickloom.Windows;

namespace Clickloom.App;

public partial class MainWindow
{
    private string? _inputTestDeviceKey;

    private void InputTest_Click(object sender, RoutedEventArgs e)
    {
        if (_inputTestDeviceKey is not null)
        {
            CloseInputTest();
            return;
        }

        InputTestResult.Visibility = Visibility.Visible;
        if (_service is not { } service || _deviceActivity is null
            || _selectedDevice?.DeviceKey is not { } key || !_connections.Any(connection => connection.Key == key))
        {
            InputTestResult.Text = "Input testing is available for connected mice when Windows Raw Input is available.";
            return;
        }
        if (service.IsEnabled)
        {
            InputTestResult.Text = "Pause Moushift before testing a button.";
            return;
        }

        _inputTestDeviceKey = key;
        InputTestButton.Content = "Stop button test";
        InputTestResult.Text = "Switch to another app, press a button or scroll, then return. If nothing appears, Windows has not reported a standard mouse input.";
    }

    private void ObserveInputTest(RawMouseButtonEvent input)
    {
        if (!input.Pressed || _inputTestDeviceKey is not { } key || _service is not { } service) return;
        if (service.IsEnabled)
        {
            CloseInputTest();
            return;
        }
        if (service.InspectStandardInput(input, key) is not { } result) return;

        string assignment = result.Action.Kind == ActionKind.OpenResource ? "Open resource" : DescribeAction(result.Action);
        var profile = result.CircleProfileName is { } circleName && circleName != result.BindingProfileName
            ? $"{result.BindingProfileName} · Circle: {circleName}"
            : result.BindingProfileName;
        InputTestResult.Text = $"{InputPresentation.Name(input.Control)} · {result.ApplicationName}\n"
            + $"Profile: {profile} · Assignment: {assignment}\n{result.Support}";
    }

    private void CloseInputTest()
    {
        _inputTestDeviceKey = null;
        InputTestButton.Content = "Test a button";
        InputTestResult.Visibility = Visibility.Collapsed;
        InputTestResult.Text = "";
    }
}
