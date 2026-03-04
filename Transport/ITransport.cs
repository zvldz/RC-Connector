using System;

namespace RcConnector.Transport
{
    /// <summary>
    /// Common interface for ESP32 data sources (Serial, BLE, UDP).
    /// </summary>
    internal interface ITransport : IDisposable
    {
        /// <summary>Transport display name (e.g. "COM14", "ESP32-BLE", "UDP:14552").</summary>
        string DisplayName { get; }

        /// <summary>True when transport is connected and ready to receive data.</summary>
        bool IsConnected { get; }

        /// <summary>Fired when data arrives from ESP32. String may contain partial lines.</summary>
        event Action<string> DataReceived;

        /// <summary>Fired when transport disconnects unexpectedly.</summary>
        event Action<string> Disconnected;

        /// <summary>Open / connect the transport.</summary>
        void Connect();

        /// <summary>Close / disconnect the transport.</summary>
        void Disconnect();
    }
}
