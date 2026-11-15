using System.Configuration;
using System.Data;
using System.Windows;

namespace ZekPcSayac
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = @"Local\ZekPcSayac_SingleInstance";

        // Static tutulur ki GC toplamasin ve uygulama omru boyunca mutex sahipligi surussun.
        private static Mutex? _singleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
            if (!createdNew)
            {
                // Zaten calisan bir ornek var: StartupUri penceresi acilmadan kapat.
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown();
                return;
            }

            // StartupUri penceresinin acilmasi base.OnStartup icinde yapilir.
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_singleInstanceMutex != null)
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }

            base.OnExit(e);
        }
    }

}
