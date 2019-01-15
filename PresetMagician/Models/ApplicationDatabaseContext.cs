using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Common;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Catel;
using Drachenkatze.PresetMagician.Utils;
using Drachenkatze.PresetMagician.VendorPresetParser;
using MethodTimer;
using PresetMagician.Migrations;
using PresetMagician.Models.EventArgs;
using PresetMagician.Services.EventArgs;
using SharedModels;
using SQLite.CodeFirst;

namespace PresetMagician.Models
{
    public class ApplicationDatabaseContext : DbContext, IPresetDataStorer
    {
        private int persistPresetCount;
        private const int PersistInterval = 400;
        public event EventHandler<PresetUpdatedEventArgs> PresetUpdated;
        public bool CompressPresetData { private get; set; }
        private readonly List<(Preset preset, byte[] presetData)> presetDataList = new List<(Preset preset, byte[] presetData)>();

        private ApplicationDatabaseContext(DbConnection existingConnection, bool contextOwnsConnection) : base(
            existingConnection, contextOwnsConnection)
        {
            Configuration.LazyLoadingEnabled = false;
            Configuration.ValidateOnSaveEnabled = false;
        }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            var sqliteConnectionInitializer =
                new SqliteCreateDatabaseIfNotExists<ApplicationDatabaseContext>(modelBuilder);

            modelBuilder.Entity<Plugin>();
            modelBuilder.Entity<Preset>();
            modelBuilder.Entity<PresetDataStorage>();
            modelBuilder.Entity<SchemaVersion>();
            Debug.WriteLine("BLA");
            Database.SetInitializer(sqliteConnectionInitializer);
        }

        

        private static string GetDatabasePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Drachenkatze\PresetMagician\PresetMagician.sqlite3");
        }

        public static ApplicationDatabaseContext Create()
        {
            var cs = new SQLiteConnectionStringBuilder()
                {DataSource = GetDatabasePath(), ForeignKeys = false, SyncMode = SynchronizationModes.Off, CacheSize = -10240};

            var context = new ApplicationDatabaseContext(new SQLiteConnection() {ConnectionString = cs.ConnectionString},
                true);
            
            context.Migrate();

            return context;
        }

        [Time]
        public void LoadPresetsForPlugin(Plugin plugin)
        {
            Configuration.AutoDetectChangesEnabled = false;
            Database.Log = Console.Write;
            plugin.PresetCache.Clear();
            var deletedPresets = (from deletedPreset in Presets where deletedPreset.Plugin.Id == plugin.Id && deletedPreset.IsDeleted select deletedPreset).AsNoTracking().ToList();
            
            foreach (var preset in deletedPresets)
            {
                plugin.PresetCache.Add((plugin.Id, preset.SourceFile), preset);
            }
            Debug.WriteLine(plugin.Presets.Count);
            Entry(plugin).Collection(p => p.Presets).Query().Where(p => !p.IsDeleted).Load();
           
            Debug.WriteLine(plugin.Presets.Count);
            foreach (var preset in plugin.Presets)
            {
                plugin.PresetCache.Add((plugin.Id, preset.SourceFile), preset);
            }

            Database.Log = null;
            Configuration.AutoDetectChangesEnabled = true;
        }


        private void SavePresetData(Preset preset, byte[] data)
        {
            var hash = HashUtils.getIxxHash(data);

            if (preset.PresetHash == hash)
            {
                return;
            }

            preset.PresetHash = hash;
            presetDataList.Add((preset, data));
        }

        public async Task PersistPreset(Preset preset, byte[] data)
        {
            Configuration.AutoDetectChangesEnabled = false;

            PresetUpdated.SafeInvoke(this, new PresetUpdatedEventArgs(preset));

            if (!preset.Plugin.PresetCache.ContainsKey((preset.Plugin.Id, preset.SourceFile)))
            {
                preset.Plugin.Presets.Add(preset);
            }
            
            SavePresetData(preset, data);

            persistPresetCount++;
            if (persistPresetCount > PersistInterval)
            {
                await Flush();
                persistPresetCount = 0;
            }
        }

        public byte[] GetPresetData(Preset preset)
        {
            using (var tempContext = Create())
            {
                return tempContext.PresetDataStorage.Find(preset.PresetId).PresetData;
            }
        }
        public async Task Flush()
        {
            using (var tempContext = Create())
            {
                tempContext.Configuration.AutoDetectChangesEnabled = false;
                foreach (var (preset, presetData) in presetDataList)
                {
                    var existingPresetData = tempContext.PresetDataStorage.Find(preset.PresetId);

                    if (existingPresetData == null)
                    {
                        existingPresetData = new PresetDataStorage {PresetDataStorageId = preset.PresetId};

                        tempContext.PresetDataStorage.Add(existingPresetData);
                    }

                    existingPresetData.IsCompressed = CompressPresetData;
                    existingPresetData.PresetData = presetData.ToArray();
                    preset.PresetSize = existingPresetData.PresetData.Length;

                    if (existingPresetData.IsCompressed)
                    {
                        preset.PresetCompressedSize = existingPresetData.CompressedPresetData.Length;
                    }


                }
                await tempContext.SaveChangesAsync();
                presetDataList.Clear();
            }

            Configuration.AutoDetectChangesEnabled = true;
            await SaveChangesAsync();
        }

        public void Migrate()
        {
            var type = typeof(IMigration);

            SchemaVersions.Load();
            var currentAssembly = Assembly.GetExecutingAssembly();

            var types = currentAssembly.GetTypes()
                .Where(p => p.GetInterfaces().Contains(type)).OrderBy(p => p.Name);

            foreach (var migration in types)
            {
                if (migration.IsAbstract)
                {
                    continue;
                }
                var migrationExecuted = (from schemaVersion in SchemaVersions where schemaVersion.Version == migration.Name select schemaVersion).Any();

                if (migrationExecuted)
                {
                    continue;
                }
                
                BaseMigration instance = (BaseMigration) Activator.CreateInstance(migration);
                instance.Database = Database;
                instance.Up();
                
                var executedMigration = new SchemaVersion();
                executedMigration.Version = migration.Name;
                //SchemaVersions.Add(executedMigration);
            }

            SaveChanges();

        }

        public DbSet<Plugin> Plugins { get; set; }
        public DbSet<Preset> Presets { get; set; }
        public DbSet<SchemaVersion> SchemaVersions { get; set; }
        public DbSet<PresetDataStorage> PresetDataStorage { get; set; }
    }
}