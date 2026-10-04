using System;
using Cosmos.HAL;

namespace WeatherOS.Services
{
    /// <summary>
    /// Service for handling hardware serial communication with the external Arduino Uno Q sensor station.
    /// Interacts directly with Cosmos.HAL.SerialPort over standard RS-232 / USB-UART (COM1-COM4).
    /// </summary>
    public class SerialSensorService
    {
        private bool _isInitialized;
        private readonly char[] _rxBuffer = new char[128];
        private int _bufferIndex = 0;

        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Initializes the hardware serial port (COM1 by default at 9600 baud).
        /// </summary>
        public bool Initialize(SerialPort.Port port = SerialPort.Port.COM1, SerialPort.BaudRate baudRate = SerialPort.BaudRate.BaudRate9600)
        {
            try
            {
                SerialPort.Enable(port, baudRate);
                _isInitialized = true;
                return true;
            }
            catch (Exception)
            {
                _isInitialized = false;
                return false;
            }
        }

        /// <summary>
        /// Tests receiving telemetry bytes from the Arduino serial stream.
        /// Reads incoming bytes until a newline delimiter is found or buffer threshold is reached.
        /// </summary>
        /// <param name="receivedLine">The reconstructed ASCII line from the Arduino.</param>
        /// <returns>True if a complete frame/line was received, false otherwise.</returns>
        public bool TryReadTelemetryPacket(out string receivedLine)
        {
            receivedLine = string.Empty;

            if (!_isInitialized)
            {
                return false;
            }

            try
            {
                // In Cosmos HAL, SerialPort.Receive() reads a byte from the serial FIFO buffer
                // Non-blocking or instant read attempt
                byte incomingByte = SerialPort.Receive();

                if (incomingByte == 0 || incomingByte == 255)
                {
                    // No valid byte currently in FIFO
                    return false;
                }

                char ch = (char)incomingByte;

                if (ch == '\n' || ch == '\r')
                {
                    if (_bufferIndex > 0)
                    {
                        receivedLine = new string(_rxBuffer, 0, _bufferIndex);
                        _bufferIndex = 0; // Reset buffer for next frame
                        return true;
                    }
                }
                else
                {
                    if (_bufferIndex < _rxBuffer.Length - 1)
                    {
                        _rxBuffer[_bufferIndex++] = ch;
                    }
                    else
                    {
                        // Buffer overflow safeguard: reset pointer
                        _bufferIndex = 0;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        /// <summary>
        /// Sends a raw command byte or probe to the Arduino station.
        /// </summary>
        public bool SendCommand(string command)
        {
            if (!_isInitialized || string.IsNullOrEmpty(command))
            {
                return false;
            }

            try
            {
                for (int i = 0; i < command.Length; i++)
                {
                    SerialPort.Send((byte)command[i]);
                }
                SerialPort.Send((byte)'\n');
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
