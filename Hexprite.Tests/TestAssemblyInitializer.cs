using System.Runtime.CompilerServices;
using Hexprite.Services;

namespace Hexprite.Tests
{
    internal static class TestAssemblyInitializer
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            LoggingService.SuppressFirstRunDialog = true;
        }
    }
}
