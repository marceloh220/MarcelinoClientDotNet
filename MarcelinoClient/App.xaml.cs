using System.Windows;
using System.Windows.Threading;

namespace MarcelinoClient;

public partial class App : Application
{
	private Mutex? _instanceMutex;
	private EventWaitHandle? _activationEvent;
	private RegisteredWaitHandle? _activationWait;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		_instanceMutex = new Mutex(false, @"Local\MarcelinoClientSingleInstance", out var created);
		if (!created)
		{
			if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
			{
				try
				{
					using var activation = EventWaitHandle.OpenExisting(@"Local\MarcelinoClientActivate");
					activation.Set();
				}
				catch (WaitHandleCannotBeOpenedException)
				{
				}
			}

			Shutdown();
			return;
		}

		var window = new MainWindow();
		MainWindow = window;
		_activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\MarcelinoClientActivate");
		_activationWait = ThreadPool.RegisterWaitForSingleObject(_activationEvent, (_, _) =>
		{
			if (!Dispatcher.HasShutdownStarted)
			{
				Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(window.RestoreFromTray));
			}
		}, null, Timeout.Infinite, executeOnlyOnce: false);
		window.Show();
		if (e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
		{
			window.StartInTray();
		}
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_activationWait?.Unregister(null);
		_activationEvent?.Dispose();
		_instanceMutex?.Dispose();
		base.OnExit(e);
	}
}

