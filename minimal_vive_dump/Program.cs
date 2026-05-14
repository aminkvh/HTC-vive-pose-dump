using System;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Valve.VR;

internal static class Program
{
    private static volatile bool _shouldStop;

    private static int Main(string[] args)
    {
        var options = ParseOptions(args);

        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        Console.CancelKeyPress += OnCancelKeyPress;

        var initError = EVRInitError.None;
        var vrSystem = OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Background);

        if (initError != EVRInitError.None || vrSystem == null)
        {
            Console.Error.WriteLine($"OpenVR init failed: {OpenVR.GetStringForHmdError(initError)} ({initError})");
            return 1;
        }

        try
        {
            Console.WriteLine("Connected to OpenVR runtime.");
            Console.WriteLine(options.RunOnce
                ? "Printing a single tracked-device pose snapshot."
                : "Printing valid tracked-device poses. Press Ctrl+C to stop.");
            Console.WriteLine($"Sampling interval: {options.IntervalMs} ms ({1000.0 / options.IntervalMs:F2} Hz)");
            Console.WriteLine($"Performance counter frequency: {Stopwatch.Frequency} ticks/sec");

            if (!string.IsNullOrWhiteSpace(options.CsvPath))
            {
                Console.WriteLine($"CSV logging enabled: {Path.GetFullPath(options.CsvPath)}");
            }

            Console.WriteLine();

            DumpLoop(vrSystem, options);
            return 0;
        }
        finally
        {
            OpenVR.Shutdown();
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("vive-pose-dump");
        Console.WriteLine("Print tracked OpenVR device positions and quaternions.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  vive-pose-dump [--once] [--interval-ms <ms>] [--csv <path>]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --once              Capture one snapshot and exit.");
        Console.WriteLine("  --interval-ms <ms>  Sampling interval in milliseconds. Default: 200.");
        Console.WriteLine("  --csv <path>        Append output rows to a CSV file.");
        Console.WriteLine("  --help              Show this help text.");
    }

    private static void DumpLoop(CVRSystem vrSystem, AppOptions options)
    {
        var poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        var csvWriter = CreateCsvWriter(options.CsvPath);
        var startCounter = Stopwatch.GetTimestamp();

        try
        {
            while (!_shouldStop)
            {
                vrSystem.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, poses);
                var counter = Stopwatch.GetTimestamp();
                var timestampUtc = DateTimeOffset.UtcNow;
                var elapsedSeconds = (double)(counter - startCounter) / Stopwatch.Frequency;
                var printedAny = false;

                for (uint deviceIndex = 0; deviceIndex < poses.Length; deviceIndex++)
                {
                    var pose = poses[deviceIndex];
                    if (!pose.bDeviceIsConnected || !pose.bPoseIsValid)
                    {
                        continue;
                    }

                    var matrix = pose.mDeviceToAbsoluteTracking;
                    var position = ExtractPosition(matrix);
                    var rotation = ExtractRotation(matrix);
                    var label = BuildDeviceLabel(vrSystem, deviceIndex);

                    Console.WriteLine(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "utc={0:O}\telapsed={1:F3}s\tcounter={2}\tfreq={3}\tdevice={4}\ttype={5}\tserial={6}\tpos=({7:F4}, {8:F4}, {9:F4})\tquat=({10:F5}, {11:F5}, {12:F5}, {13:F5})",
                            timestampUtc,
                            elapsedSeconds,
                            counter,
                            Stopwatch.Frequency,
                            deviceIndex,
                            label.DeviceClass,
                            label.SerialNumber,
                            position.X,
                            position.Y,
                            position.Z,
                            rotation.W,
                            rotation.X,
                            rotation.Y,
                            rotation.Z));

                    WriteCsvRow(csvWriter, timestampUtc, elapsedSeconds, counter, deviceIndex, label, position, rotation);
                    printedAny = true;
                }

                if (!printedAny)
                {
                    Console.WriteLine(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "utc={0:O}\telapsed={1:F3}s\tcounter={2}\tfreq={3}\tno valid tracked poses",
                            timestampUtc,
                            elapsedSeconds,
                            counter,
                            Stopwatch.Frequency));
                }

                if (options.RunOnce)
                {
                    break;
                }

                Thread.Sleep(options.IntervalMs);
            }
        }
        finally
        {
            if (csvWriter != null)
            {
                csvWriter.Dispose();
            }
        }
    }

    private static AppOptions ParseOptions(string[] args)
    {
        var options = new AppOptions(runOnce: false, intervalMs: 200, csvPath: string.Empty, showHelp: false);

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase)
                || string.Equals(argument, "/?", StringComparison.OrdinalIgnoreCase))
            {
                options = new AppOptions(runOnce: options.RunOnce, intervalMs: options.IntervalMs, csvPath: options.CsvPath, showHelp: true);
                continue;
            }

            if (string.Equals(argument, "--once", StringComparison.OrdinalIgnoreCase))
            {
                options = new AppOptions(runOnce: true, intervalMs: options.IntervalMs, csvPath: options.CsvPath, showHelp: options.ShowHelp);
                continue;
            }

            if (string.Equals(argument, "--interval-ms", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                if (int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var intervalMs) && intervalMs > 0)
                {
                    options = new AppOptions(runOnce: options.RunOnce, intervalMs: intervalMs, csvPath: options.CsvPath, showHelp: options.ShowHelp);
                    index++;
                }

                continue;
            }

            if (string.Equals(argument, "--csv", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                var csvPath = args[index + 1];
                if (!string.IsNullOrWhiteSpace(csvPath))
                {
                    options = new AppOptions(runOnce: options.RunOnce, intervalMs: options.IntervalMs, csvPath: csvPath, showHelp: options.ShowHelp);
                    index++;
                }
            }
        }

        return options;
    }

    private static (float X, float Y, float Z) ExtractPosition(HmdMatrix34_t matrix)
    {
        return (matrix.m3, matrix.m7, matrix.m11);
    }

    private static (double W, double X, double Y, double Z) ExtractRotation(HmdMatrix34_t matrix)
    {
        var w = Math.Sqrt(Math.Max(0d, 1d + matrix.m0 + matrix.m5 + matrix.m10)) / 2d;
        var x = Math.Sqrt(Math.Max(0d, 1d + matrix.m0 - matrix.m5 - matrix.m10)) / 2d;
        var y = Math.Sqrt(Math.Max(0d, 1d - matrix.m0 + matrix.m5 - matrix.m10)) / 2d;
        var z = Math.Sqrt(Math.Max(0d, 1d - matrix.m0 - matrix.m5 + matrix.m10)) / 2d;

        x = CopySign(x, matrix.m9 - matrix.m6);
        y = CopySign(y, matrix.m2 - matrix.m8);
        z = CopySign(z, matrix.m4 - matrix.m1);

        return (w, x, y, z);
    }

    private static double CopySign(double value, float sign)
    {
        return Math.Abs(value) * (sign < 0 ? -1d : 1d);
    }

    private static StreamWriter CreateCsvWriter(string csvPath)
    {
        if (string.IsNullOrWhiteSpace(csvPath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(csvPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var fileExists = File.Exists(fullPath);
        var writer = new StreamWriter(fullPath, append: true, Encoding.UTF8);
        if (!fileExists || new FileInfo(fullPath).Length == 0)
        {
            writer.WriteLine("timestamp_utc,elapsed_seconds,counter_ticks,counter_frequency_hz,device_index,device_class,serial_number,pos_x,pos_y,pos_z,quat_w,quat_x,quat_y,quat_z");
            writer.Flush();
        }

        return writer;
    }

    private static void WriteCsvRow(
        StreamWriter csvWriter,
        DateTimeOffset timestampUtc,
        double elapsedSeconds,
        long counter,
        uint deviceIndex,
        DeviceLabel label,
        (float X, float Y, float Z) position,
        (double W, double X, double Y, double Z) rotation)
    {
        if (csvWriter == null)
        {
            return;
        }

        csvWriter.WriteLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "{0:O},{1:F6},{2},{3},{4},{5},{6},{7:F6},{8:F6},{9:F6},{10:F8},{11:F8},{12:F8},{13:F8}",
                timestampUtc,
                elapsedSeconds,
                counter,
                Stopwatch.Frequency,
                deviceIndex,
                EscapeCsv(label.DeviceClass),
                EscapeCsv(label.SerialNumber),
                position.X,
                position.Y,
                position.Z,
                rotation.W,
                rotation.X,
                rotation.Y,
                rotation.Z));
        csvWriter.Flush();
    }

    private static string EscapeCsv(string value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static DeviceLabel BuildDeviceLabel(CVRSystem vrSystem, uint deviceIndex)
    {
        var deviceClass = vrSystem.GetTrackedDeviceClass(deviceIndex).ToString();
        var serialNumber = GetTrackedDeviceString(vrSystem, deviceIndex, ETrackedDeviceProperty.Prop_SerialNumber_String);

        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            serialNumber = "unknown";
        }

        return new DeviceLabel(deviceClass, serialNumber);
    }

    private static string GetTrackedDeviceString(CVRSystem vrSystem, uint deviceIndex, ETrackedDeviceProperty property)
    {
        var error = ETrackedPropertyError.TrackedProp_Success;
        var builder = new StringBuilder(128);
        vrSystem.GetStringTrackedDeviceProperty(deviceIndex, property, builder, (uint)builder.Capacity, ref error);

        if (error != ETrackedPropertyError.TrackedProp_Success)
        {
            return string.Empty;
        }

        return builder.ToString();
    }

    private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs eventArgs)
    {
        eventArgs.Cancel = true;
        _shouldStop = true;
    }

    private struct DeviceLabel
    {
        public DeviceLabel(string deviceClass, string serialNumber)
        {
            DeviceClass = deviceClass;
            SerialNumber = serialNumber;
        }

        public string DeviceClass { get; }

        public string SerialNumber { get; }
    }

    private struct AppOptions
    {
        public AppOptions(bool runOnce, int intervalMs, string csvPath, bool showHelp)
        {
            RunOnce = runOnce;
            IntervalMs = intervalMs;
            CsvPath = csvPath;
            ShowHelp = showHelp;
        }

        public bool RunOnce { get; }

        public int IntervalMs { get; }

        public string CsvPath { get; }

        public bool ShowHelp { get; }
    }
}