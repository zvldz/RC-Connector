using System;
using System.Text.Json.Serialization;

namespace RcConnector.Core
{
    /// <summary>
    /// What type of input is assigned to an RC channel.
    /// </summary>
    internal enum ChannelSourceType { None, Axis, ButtonGroup }

    /// <summary>
    /// Which joystick axis (winmm JOYINFOEX fields).
    /// </summary>
    internal enum JoystickAxis { X, Y, Z, R, U, V }

    /// <summary>
    /// Mapping configuration for a single RC channel.
    /// </summary>
    internal sealed class ChannelMapping
    {
        public ChannelSourceType SourceType { get; set; } = ChannelSourceType.None;

        /// <summary>Axis to read when SourceType == Axis.</summary>
        public JoystickAxis Axis { get; set; } = JoystickAxis.X;

        /// <summary>Invert axis direction (swap min/max).</summary>
        public bool Invert { get; set; } = false;

        /// <summary>
        /// Button indices (0-based) assigned when SourceType == ButtonGroup.
        /// N buttons produce N+1 PWM positions evenly distributed 1000..2000.
        /// No button pressed = position 0 (1000), button[0] = position 1, etc.
        /// </summary>
        public int[] Buttons { get; set; } = Array.Empty<int>();
    }

    /// <summary>
    /// Complete joystick-to-RC channel mapping (8 channels).
    /// Channels 9-16 are always 0 (MAVLink passthrough).
    /// </summary>
    internal sealed class JoystickMapping
    {
        public const int NUM_MAPPED_CHANNELS = 8;

        public ChannelMapping[] Channels { get; set; } = CreateDefault();

        /// <summary>
        /// Default mapping: axes X..V on channels 1-6, channels 7-8 unmapped.
        /// </summary>
        public static ChannelMapping[] CreateDefault()
        {
            var channels = new ChannelMapping[NUM_MAPPED_CHANNELS];
            for (int i = 0; i < NUM_MAPPED_CHANNELS; i++)
            {
                if (i < 6)
                {
                    channels[i] = new ChannelMapping
                    {
                        SourceType = ChannelSourceType.Axis,
                        Axis = (JoystickAxis)i,
                    };
                }
                else
                {
                    channels[i] = new ChannelMapping();
                }
            }
            return channels;
        }

        /// <summary>
        /// Calculate PWM value for a button group channel.
        /// N buttons → N+1 positions evenly spaced from 1000 to 2000.
        /// Returns 1000 when no button is pressed.
        /// </summary>
        public static int ButtonGroupToPwm(int[] buttons, uint dwButtons)
        {
            if (buttons.Length == 0)
                return 0;

            // Find which button (if any) is pressed — last pressed wins
            int activePosition = 0; // 0 = no button pressed
            for (int i = 0; i < buttons.Length; i++)
            {
                if ((dwButtons & (1u << buttons[i])) != 0)
                    activePosition = i + 1;
            }

            // N buttons → N+1 positions: 1000, 1000+step, 1000+2*step, ... 2000
            int numPositions = buttons.Length + 1;
            double step = 1000.0 / (numPositions - 1);
            int pwm = 1000 + (int)Math.Round(activePosition * step);
            return Math.Clamp(pwm, 1000, 2000);
        }
    }
}
