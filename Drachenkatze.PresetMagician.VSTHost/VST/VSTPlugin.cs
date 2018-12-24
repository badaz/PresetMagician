﻿using Drachenkatze.PresetMagician.Controls.Controls.VSTHost;
using Jacobi.Vst.Core;
using Jacobi.Vst.Interop.Host;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Drachenkatze.PresetMagician.VSTHost.VST
{
    

    /// <summary>
    /// Contains a VSTPlugin Plugin and utility functions like MIDI calling etc.
    /// </summary>
    public class VSTPlugin : INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public int PluginId { get; set; }

        public enum PluginTypes
        {
            Effect,
            Instrument,
            Unknown
        }

        public PluginTypes PluginType { get; set; } = PluginTypes.Unknown;

        public String PluginTypeDescription
        {
            get
            {
                return PluginType.ToString();
            }
        }

        public const bool PresetChunk_UseCurrentProgram = false;

        public Boolean IsOpened;

        public VstPluginContext PluginContext = null;

        public VstPluginFlags PluginFlags;

        public VSTPlugin(String dllPath)
        {
            PluginDLLPath = dllPath;
        }

        public bool ChunkSupport { get; set; }
        public bool IsSupported { get; set; }

        public bool IsLoaded
        {
            get
            {
                if (PluginContext != null)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public String LoadError { get; set; }

        public int NumPresets
        {
            get; set;
        }

        public String PluginDLLPath { get; set; }

        public String PluginName
        {
            get; set;
        }

        public String PluginVendor
        {
            get; set;
        }

        public void Dispose()
        {
            if (PluginContext != null)
            {
                PluginContext.Dispose();
            }
        }

        public void doCache()
        {
            this.PluginName = PluginContext.PluginCommandStub.GetEffectName();
            this.NumPresets = PluginContext.PluginInfo.ProgramCount;
            this.PluginVendor = PluginContext.PluginCommandStub.GetVendorString();
            this.PluginId = PluginContext.PluginInfo.PluginID;

            if (PluginContext.PluginInfo.Flags.HasFlag(VstPluginFlags.IsSynth))
            {
                this.PluginType = PluginTypes.Instrument;
            }
            else
            {
                this.PluginType = PluginTypes.Effect;
            }

            // Scan for preset implementations here
        }

        

        public void LoadFXP(string filePath)
        {
            if (filePath == null || filePath == "")
            {
                return;
            }
            // How does the GetChunk/SetChunk interface work? What information should be in those chunks?
            // How does the BeginLoadProgram and BeginLoadBank work?
            // There doesn't seem to be any restriction on what data is put in the chunks.
            // The beginLoadBank/Program methods are also part of the persistence call sequence.
            // GetChunk returns a buffer with program information of either the current/active program
            // or all programs.
            // SetChunk should read this information back in and initialize either the current/active program
            // or all programs.
            // Before SetChunk is called, the beginLoadBank/Program method is called
            // passing information on the version of the plugin that wrote the data.
            // This will allow you to support older data versions of your plugin's data or
            // even support reading other plugin's data.
            // Some hosts will call GetChunk before calling beginLoadBakn/Program and SetChunk.
            // This is an optimazation of the host to determine if the information to load is
            // actually different than the state your plugin program(s) (are) in.

            bool UseChunk = false;
            if ((PluginContext.PluginInfo.Flags & VstPluginFlags.ProgramChunks) == 0)
            {
                // Chunks not supported.
                UseChunk = false;
            }
            else
            {
                // Chunks supported.
                UseChunk = true;
            }

            var fxp = new FXP();
            fxp.ReadFile(filePath);
            if (fxp.ChunkMagic != "CcnK")
            {
                // not a fxp or fxb file
                Console.Out.WriteLine("Error - Cannot Load. Loaded preset is not a fxp or fxb file");
                return;
            }

            int pluginUniqueID = PluginIDStringToIDNumber(fxp.FxID);
            int currentPluginID = PluginContext.PluginInfo.PluginID;
            if (pluginUniqueID != currentPluginID)
            {
                Console.Out.WriteLine("Error - Cannot Load. Loaded preset has another ID!");
            }
            else
            {
                // Preset (Program) (.fxp) with chunk (magic = 'FPCh')
                // Bank (.fxb) with chunk (magic = 'FBCh')
                if (fxp.FxMagic == "FPCh" || fxp.FxMagic == "FBCh")
                {
                    UseChunk = true;
                }
                else
                {
                    UseChunk = false;
                }
                if (UseChunk)
                {
                    // If your plug-in is configured to use chunks
                    // the Host will ask for a block of memory describing the current
                    // plug-in state for saving.
                    // To restore the state at a later stage, the same data is passed
                    // back to setChunk.
                    byte[] chunkData = fxp.ChunkDataByteArray;
                    bool beginSetProgramResult = PluginContext.PluginCommandStub.BeginSetProgram();
                    int iResult = PluginContext.PluginCommandStub.SetChunk(chunkData, true);
                    bool endSetProgramResult = PluginContext.PluginCommandStub.EndSetProgram();
                }
                else
                {
                    // Alternatively, when not using chunk, the Host will simply
                    // save all parameter values.
                    float[] parameters = fxp.Parameters;
                    bool beginSetProgramResult = PluginContext.PluginCommandStub.BeginSetProgram();
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        PluginContext.PluginCommandStub.SetParameter(i, parameters[i]);
                    }
                    bool endSetProgramResult = PluginContext.PluginCommandStub.EndSetProgram();
                }
            }
        }

        

        public void SaveFXP(string filePath)
        {
            bool UseChunk = false;
            if ((PluginContext.PluginInfo.Flags & VstPluginFlags.ProgramChunks) == 0)
            {
                // Chunks not supported.
                UseChunk = false;
            }
            else
            {
                // Chunks supported.
                UseChunk = true;
            }

            var fxp = new FXP();
            fxp.ChunkMagic = "CcnK";
            fxp.ByteSize = 0; // will be set correctly by FXP class

            if (UseChunk)
            {
                // Preset (Program) (.fxp) with chunk (magic = 'FPCh')
                fxp.FxMagic = "FPCh"; // FPCh = FXP (preset), FBCh = FXB (bank)
                fxp.Version = 1; // Format Version (should be 1)
                fxp.FxID = PluginIDNumberToIDString(PluginContext.PluginInfo.PluginID);
                fxp.FxVersion = PluginContext.PluginInfo.PluginVersion;
                fxp.ProgramCount = PluginContext.PluginInfo.ProgramCount;
                fxp.Name = PluginContext.PluginCommandStub.GetProgramName();

                byte[] chunkData = PluginContext.PluginCommandStub.GetChunk(true);
                fxp.ChunkSize = chunkData.Length;
                fxp.ChunkDataByteArray = chunkData;
            }
            else
            {
                // Preset (Program) (.fxp) without chunk (magic = 'FxCk')
                fxp.FxMagic = "FxCk"; // FxCk = FXP (preset), FxBk = FXB (bank)
                fxp.Version = 1; // Format Version (should be 1)
                fxp.FxID = PluginIDNumberToIDString(PluginContext.PluginInfo.PluginID);
                fxp.FxVersion = PluginContext.PluginInfo.PluginVersion;
                fxp.ParameterCount = PluginContext.PluginInfo.ParameterCount;
                fxp.Name = PluginContext.PluginCommandStub.GetProgramName();

                // variable no. of parameters
                var parameters = new float[fxp.ParameterCount];
                for (int i = 0; i < fxp.ParameterCount; i++)
                {
                    parameters[i] = PluginContext.PluginCommandStub.GetParameter(i);
                }
                fxp.Parameters = parameters;
            }
            fxp.WriteFile(filePath);
        }

        public void SetProgram(int programNumber)
        {
            if (programNumber < PluginContext.PluginInfo.ProgramCount && programNumber >= 0)
            {
                PluginContext.PluginCommandStub.SetProgram(programNumber);
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static string PluginIDNumberToIDString(int pluginUniqueID)
        {
            byte[] fxIdArray = BitConverter.GetBytes(pluginUniqueID);
            Array.Reverse(fxIdArray);
            string fxIdString = BinaryFile.ByteArrayToString(fxIdArray);
            return fxIdString;
        }

        private static int PluginIDStringToIDNumber(string fxIdString)
        {
            byte[] pluginUniqueIDArray = BinaryFile.StringToByteArray(fxIdString); // 58h8 = 946354229
            Array.Reverse(pluginUniqueIDArray);
            int pluginUniqueID = BitConverter.ToInt32(pluginUniqueIDArray, 0);
            return pluginUniqueID;
        }

        
    }
}