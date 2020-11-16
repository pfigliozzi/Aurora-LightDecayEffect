using Aurora.EffectsEngine;
using Aurora;
using Aurora.Settings.Overrides;
using Aurora.EffectsEngine;
using Aurora.Profiles;
using Aurora.Devices;
using Aurora.Utils;
using Aurora.Settings;
using System.Collections.Generic;
using System.Diagnostics;
using SharpDX.RawInput;



public class LightDecayEffect : IEffectScript
{
    public string ID { get; private set; }

    // Required declarations for a ScriptLayer.
    public VariableRegistry Properties { get; private set; }
    private VariableRegistry settings;
    public KeySequence DefaultKeys = new KeySequence();

    // Store each pressed key and its progress through the gradient
    public Dictionary<DeviceKeys, KeyProgress> pressedKeys = new Dictionary<DeviceKeys, KeyProgress>();

    // Variables to record time between frames.
    private long prev_ms = Time.GetMillisecondsSinceEpoch();
    private long curr_ms = Time.GetMillisecondsSinceEpoch();
    private long elapsed_ms = 0;

    // Used to calculate the number of colors to use in the effect.
    private RealColor black = new RealColor(System.Drawing.Color.FromArgb(0, 0, 0));

    // A list of keys pressed this frame.
    private List<DeviceKeys> activeKeys = new List<DeviceKeys>();

    // A structure to store the current color of the key and its progress through the gradient.
    public struct KeyProgress
    {
        public long currColor;
        public double _Progress;
    }

    public LightDecayEffect()
    {
        ID = "LightDecayEffect";

        // Define the properties exposed to the user.
        Properties = new VariableRegistry();
        Properties.Register("baseColor", new RealColor(System.Drawing.Color.FromArgb(255, 0, 0)), "Base Color");
        Properties.Register("color1", new RealColor(System.Drawing.Color.FromArgb(255, 255, 0)), "color1");
        Properties.Register("color2", new RealColor(System.Drawing.Color.FromArgb(0, 255, 0)), "color2");
        Properties.Register("color3", new RealColor(System.Drawing.Color.FromArgb(0, 0, 255)), "color3");
        Properties.Register("color4", new RealColor(System.Drawing.Color.FromArgb(255, 0, 255)), "color4");
        Properties.Register("kick", 1, "Kick towards higher color", 4, 1, "Each keypress will kick this this many colors above the base color.");
        Properties.Register("decay", 1500, "Decay Time", null, 100, "Time it takes to decay to next color (milliseconds).");

        // Start listening for keydown events.
        Global.InputEvents.KeyDown += InputEvents_KeyDown;
    }

    // Capture keydown event and check if the pressed key is part of the sequence.
    private void InputEvents_KeyDown(object sender, KeyboardInputEventArgs e)
    {
        activeKeys.Add(e.GetDeviceKey());
    }
    // Called every keyboard "frame" and updates the colors.
    public object UpdateLights(VariableRegistry settings, IGameState state = null)
    {
        // Store the settings because these settings are the ones the user can change, and the `Properties` field isn't.
        this.settings = settings;

        // Unpack settings needed for updating lights
        int numberOfColors = getNumberOfColorsUsed(settings);
        long decay_ms = settings.GetVariable<long>("decay");
        long kick = settings.GetVariable<long>("kick");
        RealColor[] gradientColors = getGradientColors(settings); // Get the colors to use from the GUI.

        long ms = msSinceLastFrame();

        // Create a layer to apply the effects to.
        EffectLayer layer = new EffectLayer(ID);

        // If not tracking any pressed keys and no keys were pressed this frame, return the base color.
        layer.Fill(gradientColors[0].GetDrawingColor());
        if (pressedKeys.Count == 0 && activeKeys.Count == 0)
        {
            return layer;
        }

        // Add key if it hasn't been pressed, update it if it was already pressed.
        foreach (DeviceKeys activeKey in activeKeys)
        {
            if (pressedKeys.ContainsKey(activeKey))
            {
                KeyProgress key_prev_state = pressedKeys[activeKey];
                long currColor = key_prev_state.currColor;
                long currKick = kick;
                key_prev_state.currColor = currColor + currKick > numberOfColors ? numberOfColors : currColor + currKick;
                key_prev_state._Progress = 1.0;
                pressedKeys[activeKey] = key_prev_state;
            }
            else
            {
                KeyProgress new_key;
                new_key.currColor = kick;
                new_key._Progress = 1.0;
                pressedKeys.Add(activeKey, new_key);
            }
        }

        // Decreament all pressedKeys that were not pressed this frame.
        List<DeviceKeys> keysList = new List<DeviceKeys>(pressedKeys.Keys);
        foreach (DeviceKeys pressedKey in keysList)
        {
            if (activeKeys.Contains(pressedKey))
            {
                continue;
            }
            if (pressedKeys[pressedKey].currColor == 1 && pressedKeys[pressedKey]._Progress <= 0)
            {
                pressedKeys.Remove(pressedKey);
            }
            else if (pressedKeys[pressedKey]._Progress <= 0)
            {
                KeyProgress newKeyProgress;
                newKeyProgress.currColor = pressedKeys[pressedKey].currColor - 1;
                newKeyProgress._Progress = 1.0;
                pressedKeys[pressedKey] = newKeyProgress;
            }
            else
            {
                KeyProgress newKeyProgress;
                newKeyProgress.currColor = pressedKeys[pressedKey].currColor;
                newKeyProgress._Progress = updateProgress(decay_ms, pressedKeys[pressedKey]._Progress, ms);
                pressedKeys[pressedKey] = newKeyProgress;
            }
        }

        // Set each pressedKey to the appropriate color.
        foreach (KeyValuePair<DeviceKeys, KeyProgress> kvp in pressedKeys)
        {
            System.Drawing.Color currentColor = getColorThisFrame(kvp.Value, gradientColors);
            layer.Set(kvp.Key, currentColor);
        }

        // Clear the list of keys that were pressed this frame.
        activeKeys.Clear();

        return layer;
    }
    
    // Returns the number of colors to use in the gradient. If the later colors are set to black (RGB = 0, 0, 0) then
    // they will not be used in the color gradient.
    private int getNumberOfColorsUsed(VariableRegistry settings)
    {
        int blackAsInt = ColorUtils.GetIntFromColor(black);
        if (ColorUtils.GetIntFromColor(settings.GetVariable<RealColor>("color4")) != blackAsInt)
        {
            return 4;
        }
        else if (ColorUtils.GetIntFromColor(settings.GetVariable<RealColor>("color3")) != blackAsInt)
        {
            return 3;
        }
        else if (ColorUtils.GetIntFromColor(settings.GetVariable<RealColor>("color2")) != blackAsInt)
        {
            return 2;
        }
        else if (ColorUtils.GetIntFromColor(settings.GetVariable<RealColor>("color1")) != blackAsInt)
        {
            return 1;
        }
        return 0;
    }

    // Get the gradient colors as an array in order of the gradient for the effect.
    private RealColor[] getGradientColors(VariableRegistry settings)
    {
        RealColor[] colors = new RealColor[] { settings.GetVariable<RealColor>("baseColor"),
            settings.GetVariable<RealColor>("color1"),
            settings.GetVariable<RealColor>("color2"),
            settings.GetVariable<RealColor>("color3"),
            settings.GetVariable<RealColor>("color4") };
        return colors;
    }

    // Calculates the time since last call.
    private long msSinceLastFrame()
    {
        curr_ms = Time.GetMillisecondsSinceEpoch();
        long ms = curr_ms - prev_ms;
        prev_ms = curr_ms;
        return ms;
    }

    // Updates progress for the decay effect
    private double updateProgress(long decay_ms, double currProgress, long ms)
    {
        long curr_ms = (long)(currProgress * (double)decay_ms);
        curr_ms -= ms;
        double new_progress = (double)curr_ms / (double)decay_ms;
        return new_progress;
    }

    // Returns the appropriate color for this frame based on the progress.
    private System.Drawing.Color getColorThisFrame(KeyProgress keyProgress, RealColor[] colors)
    {
        System.Drawing.Color backgroundColor = colors[keyProgress.currColor - 1].GetDrawingColor();
        System.Drawing.Color foregroundColor = colors[keyProgress.currColor].GetDrawingColor();
        System.Drawing.Color currentColor = ColorUtils.BlendColors(backgroundColor, foregroundColor, keyProgress._Progress);
        return currentColor;
    }
}