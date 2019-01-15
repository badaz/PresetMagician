﻿using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Catel.Collections;
using Drachenkatze.PresetMagician.NKSF.NKSF;
using Drachenkatze.PresetMagician.VendorPresetParser;
using Drachenkatze.PresetMagician.VSTHost.Properties;
using Jacobi.Vst.Core;
using Jacobi.Vst.Interop.Host;
using MethodTimer;
using NAudio.Wave;
using PresetMagician.Models;
using SharedModels;

namespace Drachenkatze.PresetMagician.VSTHost.VST
{
    public class VstPluginExport
    {
        
        public string UserContentDirectory { get; set; }

        public VstPluginExport(VstHost vstHost)
        {
            VstHost = vstHost;
        }

        public VstHost VstHost { get; }

      
        public void ExportPresetNKSF(Plugin plugin, Preset preset, byte[] data)
        {
            var vst = plugin;
            vst.PluginContext.PluginCommandStub.Open();

            var nksf = new NKSFRiff();
            var guid = Guid.NewGuid();
            string fileExtension;

            nksf.kontaktSound.summaryInformation.summaryInformation.vendor = plugin.PluginVendor;
            nksf.kontaktSound.summaryInformation.summaryInformation.uuid = guid;
            nksf.kontaktSound.summaryInformation.summaryInformation.name = preset.PresetName;

            if (plugin.PluginType == Plugin.PluginTypes.Instrument)
            {
                nksf.kontaktSound.summaryInformation.summaryInformation.deviceType = "INST";
                fileExtension = ".nksf";
            } else if (plugin.PluginType == Plugin.PluginTypes.Effect)
            {
                nksf.kontaktSound.summaryInformation.summaryInformation.deviceType = "FX";
                fileExtension = ".nksfx";
            }
            else
            {
                throw new ArgumentException("Unknown device type");
            }

            nksf.kontaktSound.summaryInformation.summaryInformation.bankChain.Add(plugin.PluginName);
            
            var bankPath = preset.PresetBank.GetBankPath();
            bankPath.RemoveAt(0);

            nksf.kontaktSound.summaryInformation.summaryInformation.bankChain.AddRange(bankPath);

            nksf.kontaktSound.summaryInformation.summaryInformation.Types = preset.Types;
            nksf.kontaktSound.summaryInformation.summaryInformation.Modes = preset.Modes;
            nksf.kontaktSound.summaryInformation.summaryInformation.author = preset.Author;

            nksf.kontaktSound.summaryInformation.summaryInformation.comment = preset.Comment + Environment.NewLine + "Generated with PresetMagician";
            nksf.kontaktSound.pluginId.pluginId.VSTMagic = plugin.PluginContext.PluginInfo.PluginID;
            nksf.kontaktSound.pluginChunk.PresetData = data;

            if (plugin.DefaultControllerAssignments != null)
            {
                nksf.kontaktSound.controllerAssignments.controllerAssignments =
                    plugin.DefaultControllerAssignments;
            }

            var outputFilename = Path.Combine(GetUserContentDirectory(preset),
                GetNKSFPresetName(preset.PresetName) + fileExtension);
            var fileStream2 = new FileStream(outputFilename, FileMode.Create);
            nksf.Write(fileStream2);
            fileStream2.Close();
        }

        public string GetUserContentDirectory(Preset preset)
        {
            string userContentDirectory;
            if (!Directory.Exists(UserContentDirectory))
            {
                userContentDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    @"Native Instruments\User Content");
            }
            else
            {
                userContentDirectory = UserContentDirectory;
            }

            var bankDirectory = Path.Combine(userContentDirectory, GetNKSFPluginName(preset.Plugin.PluginName),
                GetNKSFBankName(preset.PresetBank.BankName));
            Directory.CreateDirectory(bankDirectory);
            return bankDirectory;
        }

        public string GetNKSFPluginName(string pluginName)
        {
            foreach (var c in Path.GetInvalidPathChars())
            {
                pluginName = pluginName.Replace(c, '_');
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                pluginName = pluginName.Replace(c, '_');
            }

            return pluginName;
        }

        public string GetNKSFPresetName(string presetName)
        {
            // Returns the sanitized preset name

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                presetName = presetName.Replace(c, '_');
            }

            foreach (var c in Path.GetInvalidPathChars())
            {
                presetName = presetName.Replace(c, '_');
            }

            return presetName;
        }

      

        private string GetPreviewFilename(Preset vstPreset)
        {
            var bankDirectory = GetUserContentDirectory(vstPreset);

            var previewDirectory = Path.Combine(bankDirectory, ".previews");

            Directory.CreateDirectory(previewDirectory);

            return Path.Combine(previewDirectory,
                GetNKSFPresetName(vstPreset.PresetName));
        }

        public string GetNKSFBankName(string bankName)
        {
            foreach (var c in Path.GetInvalidPathChars())
            {
                bankName = bankName.Replace(c, '_');
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                bankName = bankName.Replace(c, '_');
            }

            return bankName;
        }

        private void ConvertToOGG(string inputWave, string outputOGG)
        {
            var path = Path.Combine(Path.GetTempPath(), "ffmpeg.exe");

            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, Resources.ffmpeg);
            }

            var process = new Process
            {
                StartInfo =
                {
                    FileName = path,
                    Arguments = "-i \"" + inputWave + "\" -acodec libvorbis -y \"" + outputOGG + "\"",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };

            Debug.WriteLine("ffmpeg path: " + process.StartInfo.FileName);
            Debug.WriteLine("ffmpeg arguments: " + process.StartInfo.Arguments);

            process.Start();
            process.WaitForExit();
            process.Close();
            process.Dispose();

        }

        [Time]
        public bool ExportPresetAudioPreviewRealtime(Plugin plugin, Preset preset, byte[] data)
        {
            var blockSize = VstHost.BlockSize;
            
            var vst = plugin;

            var ctx = vst.PluginContext;

            if ((ctx.PluginCommandStub.PluginContext.PluginInfo.Flags & VstPluginFlags.IsSynth) == 0)
            {
                throw new EffectsNotSupportedException();
            }

            // check if the plugin supports real time processing
            if (ctx.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.NoRealTime)) == VstCanDoResult.Yes)
            {
                throw new NoRealtimeProcessingException();
            }

            

            ctx.PluginCommandStub.SetChunk(data, false);

            var outputCount = ctx.PluginInfo.AudioOutputCount;
            var inputCount = ctx.PluginInfo.AudioInputCount;
            

            var tempFileName = GetPreviewFilename(preset)+".nksf.wav";

            using (var inputMgr = new VstAudioBufferManager(inputCount, blockSize))
            {
                using (var outputMgr = new VstAudioBufferManager(outputCount, blockSize))
                {
                    

                    var outputBuffers = outputMgr.ToArray();
                    var inputBuffers = inputMgr.ToArray();

                    var p = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                    var targetLength = 6;
                    var noteOffSecond = 1;
                    var loops = 44100 * targetLength / blockSize;
                    var noteOffLoop = 44100 * noteOffSecond / blockSize;
                                        
                    var initialDelay = plugin.PresetParserAudioPreviewPreDelay;

                    if (plugin.AudioPreviewPreDelay != 0)
                    {
                        initialDelay = plugin.AudioPreviewPreDelay;
                    }
                    
                    Debug.WriteLine("Using initial delay of "+initialDelay);
                    
                    var writer = new WaveFileWriter(tempFileName, p);

                    
                    // Empty buffer
                    int k;
                                
                    for (k = 0; k < initialDelay; k++)
                    {
                        ctx.PluginCommandStub.ProcessReplacing(inputBuffers, outputBuffers);
                    }
                    
                    VstHost.MIDI_NoteOn(vst, (byte) preset.PreviewNote.NoteNumber, 127);

                    
                    for (k = 0; k < loops; k++)
                    {
                        if (k == noteOffLoop)
                        {
                            VstHost.MIDI_NoteOff(vst, (byte) preset.PreviewNote.NoteNumber, 127);
                        }

                        ctx.PluginCommandStub.ProcessReplacing(inputBuffers, outputBuffers);
                        for (var j = 0; j < blockSize; j++)
                        {
                            foreach (var t in outputBuffers)
                            {
                                writer.WriteSample(t[j]);
                            }
                        }
                    }

                    writer.Close();
                }
            }

            ConvertToOGG(tempFileName, GetPreviewFilename(preset)+".nksf.ogg");
            File.Delete(tempFileName);
            return true;
        }
    }
}