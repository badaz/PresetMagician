using Catel.IoC;
using PresetMagician.Core.Services;

namespace PresetMagician.Core
{
    public static class CoreInitializer
    {
        public static void RegisterServices(IServiceLocator serviceLocator)
        {
            serviceLocator.RegisterType<GlobalService, GlobalService>();
            serviceLocator.RegisterType<GlobalFrontendService, GlobalFrontendService>();
            serviceLocator.RegisterType<DataPersisterService, DataPersisterService>();
            serviceLocator.RegisterType<PresetDataPersisterService, PresetDataPersisterService>();
            serviceLocator.RegisterType<PluginService, PluginService>();
            serviceLocator.RegisterType<CharacteristicsService, CharacteristicsService>();
            serviceLocator.RegisterType<TypesService, TypesService>();
            serviceLocator.RegisterType<VendorPresetParserService, VendorPresetParserService>();
            serviceLocator.RegisterType<RemoteVstService, RemoteVstService>();
        }
    }

    public static class Core
    {
        public static bool UseDispatcher = true;
    }
}