using Aurora.EffectsEngine;
using Aurora;
using Aurora.Settings.Overrides;
using Aurora.EffectsEngine;
using Aurora.Profiles;
using Aurora.Devices;
using Aurora.Utils;
using Aurora.Settings;
using System.Collections.Generic;
using System;
using System.Diagnostics;
using SharpDX.RawInput;
using System.Windows.Media;


public class LightDecayEffect : IEffectScript
{
    public string ID { get; private set; }

    public VariableRegistry Properties { get; private set; }

    // This is used to store the varregistry from UpdateLights, since it is NOT the same as `Properties`.
    private VariableRegistry settings;

    // Default to an empty sequence.
    public KeySequence DefaultKeys = new KeySequence();

    public Dictionary<DeviceKeys, KeyProgress> pressedKeys = new Dictionary<DeviceKeys, KeyProgress>();

    // Stopwatch for the frames
    private Stopwatch frameTimer = new Stopwatch();

    public int numberOfColors;

    private DeviceKeys activeKey;

    private RealColor black = new RealColor(System.Drawing.Color.FromArgb(0, 0, 0));

    public struct KeyProgress
    {
        public int currColor;
        public double _Progress;
    }

    public LightDecayEffect()
    {
        ID = "LightDecayEffect";

        // Define the properties exposed to the user.
        Properties = new VariableRegistry();
        Properties.Register("baseColor", new RealColor(System.Drawing.Color.FromArgb(255, 0, 0)), "Base Color");
        Properties.Register("color1", new RealColor(System.Drawing.Color.FromArgb(255, 255, 255)), "color1");
        Properties.Register("color2", new RealColor(System.Drawing.Color.FromArgb(0, 255, 0)), "color2");
        Properties.Register("color3", new RealColor(System.Drawing.Color.FromArgb(0, 0, 255)), "color3");
        Properties.Register("color4", new RealColor(System.Drawing.Color.FromArgb(255, 0, 255)), "color4");
        Properties.Register("kick", 1, "Kick towards higher color", 4, 1, "Each keypress will kick this this many colors above the base color.");
        Properties.Register("decay", 1000, "Decay Time", null, 100, "Time it takes to decay to next color (milliseconds).");
        Properties.Register("keys", new KeySequence(), "Main Keys");

        // Start listening for keydown events.
        Global.InputEvents.KeyDown += InputEvents_KeyDown;
    }

    // Capture keydown event and check if the pressed key is part of the sequence.
    private void InputEvents_KeyDown(object sender, KeyboardInputEventArgs e)
    {
        if (settings != null && settings.GetVariable<KeySequence>("keys").keys.Contains(e.GetDeviceKey()))
            activeKey = e.GetDeviceKey();
    }

    private int getNumberOfColorsUsed(VariableRegistry settings)
    {

        if (settings.GetVariable<RealColor>("color4").Equals(black))
        {
            return 5;
        }
        else if (settings.GetVariable<RealColor>("color3").Equals(black))
        {
            return 4;
        }
        else if (settings.GetVariable<RealColor>("color2").Equals(black))
        {
            return 3;
        }
        else if (settings.GetVariable<RealColor>("color1").Equals(black))
        {
            return 2;
        }
        return 1;
    }

    private double updateProgress(VariableRegistry settings, double currProgress, int ms)
    {
        int curr_ms = (int)(currProgress * settings.GetVariable<double>("decay"));
        curr_ms += ms;
        double new_progress = (double)curr_ms / (double)settings.GetVariable<double>("decay");
        return new_progress;

    }
    // Called every keyboard "frame" and updates the colours.
    public object UpdateLights(VariableRegistry settings, IGameState state = null)
    {
        // Store the settings because these settings are the ones the user can change, and the `Properties` field isn't.
        this.settings = settings;
        numberOfColors = getNumberOfColorsUsed(settings);

        // Create a layer to apply the effects to.
        EffectLayer layer = new EffectLayer(ID);

        // Calculate time since last UpdateLights call
        int ms = (int)frameTimer.ElapsedMilliseconds;
        frameTimer.Restart();

        
        if (pressedKeys.Count == 0 && activeKey == DeviceKeys.NONE)
        {
            return layer;
        }
        

        // Update the key if it is already a pressed key
        if (activeKey != DeviceKeys.NONE)
        {
            if (pressedKeys.ContainsKey(activeKey))
            {
                KeyProgress key_prev_state = pressedKeys[activeKey];
                key_prev_state.currColor += 1; //% numberOfColors;//numberOfColors;
                key_prev_state._Progress = 0;
                pressedKeys[activeKey] = key_prev_state;
            }
            else
            {
                KeyProgress new_key;
                new_key.currColor = settings.GetVariable<int>("kick");
                new_key._Progress = 0;
                pressedKeys.Add(activeKey, new_key);
            }
        }


        //List<DeviceKeys> removeKeys = new List<DeviceKeys>();
        List<DeviceKeys> keysList = new List<DeviceKeys>(pressedKeys.Keys);
        // Decreament all non-active keys
        foreach (DeviceKeys pressedKey in keysList)
        {
            if (pressedKey == activeKey)
            {
                continue;
            }
            if (pressedKeys[pressedKey].currColor == 0 && pressedKeys[pressedKey]._Progress >= 1.0)
            {
                pressedKeys.Remove(pressedKey);
            }
            else if (pressedKeys[pressedKey]._Progress >= 1.0)
            {
                KeyProgress newKeyProgress;
                newKeyProgress.currColor = pressedKeys[pressedKey].currColor - 1;
                newKeyProgress._Progress = 0;
                pressedKeys[pressedKey] = newKeyProgress;
            }
            else
            {
                KeyProgress newKeyProgress;
                newKeyProgress.currColor = pressedKeys[pressedKey].currColor;
                newKeyProgress._Progress = updateProgress(settings, pressedKeys[pressedKey]._Progress, ms);
                pressedKeys[pressedKey] = newKeyProgress;
            }
            // Figure out how to update the values in a has table.
        }


        RealColor[] colors = new RealColor[] { settings.GetVariable<RealColor>("baseColor"),
            settings.GetVariable<RealColor>("color1"),
            settings.GetVariable<RealColor>("color2"),
            settings.GetVariable<RealColor>("color3"),
            settings.GetVariable<RealColor>("color4") };

        foreach (var kvp in pressedKeys)
        {
            layer.Set(kvp.Key, colors[kvp.Value.currColor].GetDrawingColor());
        }
        activeKey = DeviceKeys.NONE;

        return layer;
    }
}