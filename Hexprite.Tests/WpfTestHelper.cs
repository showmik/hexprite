using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Hexprite.Tests
{
    public static class WpfTestHelper
    {
        private static readonly object _appLock = new object();
        private static bool _appCreated = false;
        private static Dispatcher? _staDispatcher;

        public static void EnsureApplication()
        {
            if (Application.Current == null)
            {
                lock (_appLock)
                {
                    if (!_appCreated)
                    {
                        using var readyEvent = new ManualResetEvent(false);
                        var thread = new Thread(() =>
                        {
                            try
                            {
                                var app = new Application
                                {
                                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                                };
                                app.Resources.MergedDictionaries.Add(
                                    new ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Dim.xaml", UriKind.RelativeOrAbsolute) });
                                app.Resources.MergedDictionaries.Add(
                                    new ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Styles.xaml", UriKind.RelativeOrAbsolute) });
                                _staDispatcher = Dispatcher.CurrentDispatcher;
                            }
                            catch (InvalidOperationException)
                            {
                                // Already created
                            }
                            finally
                            {
                                readyEvent.Set();
                            }
                            Dispatcher.Run();
                        });
                        thread.SetApartmentState(ApartmentState.STA);
                        thread.IsBackground = true;
                        thread.Start();
                        readyEvent.WaitOne();
                        _appCreated = true;
                    }
                }
            }
            else if (_staDispatcher != null && Application.Current.Resources.MergedDictionaries.Count == 0)
            {
                if (_staDispatcher.CheckAccess())
                {
                    Application.Current.Resources.MergedDictionaries.Add(
                        new ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Dim.xaml", UriKind.RelativeOrAbsolute) });
                    Application.Current.Resources.MergedDictionaries.Add(
                        new ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Styles.xaml", UriKind.RelativeOrAbsolute) });
                }
                else
                {
                    _staDispatcher.Invoke(() =>
                    {
                        if (Application.Current.Resources.MergedDictionaries.Count == 0)
                        {
                            Application.Current.Resources.MergedDictionaries.Add(
                                new ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Dim.xaml", UriKind.RelativeOrAbsolute) });
                            Application.Current.Resources.MergedDictionaries.Add(
                                new ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Styles.xaml", UriKind.RelativeOrAbsolute) });
                        }
                    });
                }
            }
        }

        public static void RunOnSta(Action action)
        {
            EnsureApplication();
            var dispatcher = _staDispatcher ?? Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(action);
            }
            else if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                action();
            }
            else if (dispatcher != null)
            {
                dispatcher.Invoke(action);
            }
            else
            {
                Exception? error = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
                if (error != null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
                }
            }
        }

        public static void RunOnSta(Func<Task> asyncAction)
        {
            EnsureApplication();
            var dispatcher = _staDispatcher ?? Application.Current?.Dispatcher;
            Action runner = () =>
            {
                var task = asyncAction();
                if (!task.IsCompleted)
                {
                    var frame = new DispatcherFrame();
                    task.ContinueWith(_ =>
                    {
                        frame.Continue = false;
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                    Dispatcher.PushFrame(frame);
                }
                task.GetAwaiter().GetResult();
            };

            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(runner);
            }
            else if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                runner();
            }
            else if (dispatcher != null)
            {
                dispatcher.Invoke(runner);
            }
            else
            {
                Exception? error = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        runner();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
                if (error != null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
                }
            }
        }
    }
}
