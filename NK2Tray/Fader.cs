﻿using NAudio.Midi;
using System;

namespace NK2Tray
{
    public class FaderDef
    {
        public bool delta;
        public float range;
        public int channel;
        public bool selectPresent;
        public bool mutePresent;
        public bool recordPresent;
        public int faderOffset;
        public int selectOffset;
        public int muteOffset;
        public int recordOffset;
        public MidiCommandCode faderCode;
        public MidiCommandCode selectCode;
        public MidiCommandCode muteCode;
        public MidiCommandCode recordCode;
        public int faderChannelOverride;
        public int selectChannelOverride;
        public int muteChannelOverride;
        public int recordChannelOverride;

        public FaderDef(bool _delta, float _range, int _channel,
            bool _selectPresent, bool _mutePresent, bool _recordPresent,
            int _faderOffset, int _selectOffset, int _muteOffset, int _recordOffset,
            MidiCommandCode _faderCode, MidiCommandCode _selectCode, MidiCommandCode _muteCode, MidiCommandCode _recordCode,
            int _faderChannelOverride = -1, int _selectChannelOverride = -1, int _muteChannelOverride = -1, int _recordChannelOverride = -1)
        {
            delta = _delta;
            range = _range;
            channel = _channel;
            selectPresent = _selectPresent;
            mutePresent = _mutePresent;
            recordPresent = _recordPresent;
            faderOffset = _faderOffset;
            selectOffset = _selectOffset;
            muteOffset = _muteOffset;
            recordOffset = _recordOffset;
            faderCode = _faderCode;
            selectCode = _selectCode;
            muteCode = _muteCode;
            recordCode = _recordCode;
            faderChannelOverride = _faderChannelOverride;
            selectChannelOverride = _selectChannelOverride;
            muteChannelOverride = _muteChannelOverride;
            recordChannelOverride = _recordChannelOverride;
        }
    }

    public class Fader
    {
        private bool activeHandling = false;

        private bool selectLight = false;
        private bool muteLight = false;
        private bool recordLight = false;

        public int faderNumber;
        public FaderDef faderDef;
        public MixerSession assignment;
        public bool assigned;
        public MidiOut midiOut;
        public MidiDevice parent;
        public string identifier;
        public string applicationPath;
        public string applicationName;

        public Fader(MidiDevice midiDevice, int faderNum)
        {
            parent = midiDevice;
            midiOut = midiDevice.midiOut;
            faderNumber = faderNum;
            faderDef = parent.DefaultFaderDef;
        }

        public Fader(MidiDevice midiDevice, int faderNum, FaderDef _faderDef)
        {
            parent = midiDevice;
            midiOut = midiDevice.midiOut;
            faderNumber = faderNum;
            faderDef = _faderDef;
        }

        private int inputController => faderNumber + faderDef.faderOffset;
        private int selectController => faderNumber + faderDef.selectOffset;
        private int muteController => faderNumber + faderDef.muteOffset;
        private int recordController => faderNumber + faderDef.recordOffset;


        public void ResetLights()
        {
            SetSelectLight(false);
            SetMuteLight(false);
            SetRecordLight(false);
        }

        public void Assign(MixerSession mixerSession)
        {
            assigned = true;
            assignment = mixerSession;
            identifier = mixerSession.sessionIdentifier;
            convertToApplicationPath(identifier);
            applicationName = mixerSession.label;
            SetSelectLight(true);
            SetRecordLight(false);
            if (faderDef.delta)
                parent.SetVolumeIndicator(faderNumber, mixerSession.GetVolume());
        }

        public void AssignInactive(string ident)
        {
            identifier = ident;
            convertToApplicationPath(identifier);
            assigned = false;
            SetSelectLight(true);
            SetRecordLight(true);
        }

        public void Unassign()
        {
            assigned = false;
            assignment = null;
            SetSelectLight(false);
            identifier = "";
            if (faderDef.delta)
                parent.SetVolumeIndicator(faderNumber, -1);
        }

        private void convertToApplicationPath(string ident)
        {
            if (ident != null && ident != "" && ident != "__MASTER__" && ident != "__FOCUS__") // TODO cleaner handling of special fader types
            {
                //"{0.0.0.00000000}.{...}|\\Device\\HarddiskVolume8\\Users\\Dr. Locke\\AppData\\Roaming\\Spotify\\Spotify.exe%b{00000000-0000-0000-0000-000000000000}"
                int deviceIndex = ident.IndexOf("\\Device");
                int endIndex = ident.IndexOf("%b{");
                ident = ident.Substring(deviceIndex, endIndex - deviceIndex);
                applicationPath = DevicePathMapper.FromDevicePath(ident);
            }
        }

        public void SetSelectLight(bool state)
        {
            selectLight = state;
            parent.SetLight(selectController, state);
        }

        public void SetMuteLight(bool state)
        {
            muteLight = state;
            parent.SetLight(muteController, state);
        }

        public void SetRecordLight( bool state)
        {
            recordLight = state;
            parent.SetLight(recordController, state);
        }

        public bool GetSelectLight(){ return selectLight; }
    
        public bool GetMuteLight() { return muteLight; }

        public bool GetRecordLight() { return recordLight; }

        public bool Match(int faderNumber, MidiEvent midiEvent, MidiCommandCode code, int offset, int channel=-1)
        {
            if (channel < 0)
                channel = faderDef.channel;
            if (midiEvent.Channel != channel)
                return false;
            if (midiEvent.CommandCode != code)
                return false;
            if (code == MidiCommandCode.ControlChange)
            {
                var me = (ControlChangeEvent)midiEvent;
                if ((int)me.Controller == faderNumber + offset)
                    return true;
            }
            else if (code == MidiCommandCode.NoteOn)
            {
                var me = (NoteEvent)midiEvent;
                if (me.NoteNumber == faderNumber + offset)
                    return true;
            }
            else if (code == MidiCommandCode.PitchWheelChange)
            {
                return true;
            }

            return false;
        }

        public int GetValue(MidiEvent midiEvent)
        {
            if (midiEvent.CommandCode == MidiCommandCode.ControlChange)
            {
                var me = (ControlChangeEvent)midiEvent;
                return me.ControllerValue;
            }
            else if (midiEvent.CommandCode == MidiCommandCode.NoteOn)
            {
                var me = (NoteEvent)midiEvent;
                return me.Velocity;
            }
            else if (midiEvent.CommandCode == MidiCommandCode.PitchWheelChange)
            {
                var me = (PitchWheelChangeEvent)midiEvent;
                return me.Pitch;
            }

            return 0;
        }

        public bool HandleEvent(MidiInMessageEventArgs e)
        {
            if (!IsHandling())
            {
                SetHandling(true);

                // Fader match
                if (assigned && Match(faderNumber, e.MidiEvent, faderDef.faderCode, faderDef.faderOffset, faderDef.faderChannelOverride))
                {
                    if (faderDef.delta)
                    {
                        float curVol;
                        var val = GetValue(e.MidiEvent);
                        if (val > faderDef.range / 2)
                            curVol = assignment.ChangeVolume((faderDef.range - val) / faderDef.range);
                        else
                            curVol = assignment.ChangeVolume(val / faderDef.range);
                        parent.SetVolumeIndicator(faderNumber, curVol);
                    }
                    else
                    {
                        assignment.SetVolume(GetValue(e.MidiEvent) / faderDef.range);
                    }

                    if (assignment.IsDead())
                        SetRecordLight(true);

                    return true;
                }

                // Select match
                if (Match(faderNumber, e.MidiEvent, faderDef.selectCode, faderDef.selectOffset, faderDef.selectChannelOverride))
                {
                    if (GetValue(e.MidiEvent) != 127) // Only on button-down
                        return true;

                    Console.WriteLine($@"Attempting to assign current window to fader {faderNumber}");
                    if (assigned)
                    {
                        Unassign();
                        parent.SaveAssignments();
                    }
                    else
                    {
                        var pid = WindowTools.GetForegroundPID();
                        var mixerSession = parent.audioDevice.FindMixerSessions(pid);
                        if (mixerSession != null)
                        {
                            Assign(mixerSession);
                            parent.SaveAssignments();
                        }
                        else
                            Console.WriteLine($@"MixerSession not found for pid {pid}");
                    }
                    return true;
                }

                // Mute match
                if (assigned && Match(faderNumber, e.MidiEvent, faderDef.muteCode, faderDef.muteOffset, faderDef.muteChannelOverride))
                {
                    if (GetValue(e.MidiEvent) != 127) // Only on button-down
                        return true;

                    SetMuteLight(assignment.ToggleMute());
                    if (assignment.IsDead())
                        SetRecordLight(true);
                    return true;
                }

                // Record match
                if (assigned && Match(faderNumber, e.MidiEvent, faderDef.recordCode, faderDef.recordOffset, faderDef.recordChannelOverride))
                {
                    if (GetValue(e.MidiEvent) != 127) // Only on button-down
                        return true;

                    if (WindowTools.IsProcessByNameRunning(applicationName))
                        SetRecordLight(false);
                    else
                    {
                        WindowTools.StartApplication(applicationPath);
                    }

                    return true;
                }
            }
                return false;

        }

        public bool IsHandling()
        {
            return activeHandling;
        }

        public void SetHandling(bool handling)
        {
            activeHandling = handling;
        }
    }
}
