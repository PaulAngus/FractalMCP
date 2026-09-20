namespace FractalMcp.Protocol;

public enum FractalDeviceModel
{
    AxeFxIII,
    FM3,
    FM9,
}

public static class FractalDeviceModelExtensions
{
    public static byte ModelId(this FractalDeviceModel model) => model switch
    {
        FractalDeviceModel.AxeFxIII => 0x10,
        FractalDeviceModel.FM3 => 0x11,
        FractalDeviceModel.FM9 => 0x12,
        _ => throw new ArgumentOutOfRangeException(nameof(model)),
    };

    public static string WireName(this FractalDeviceModel model) => model switch
    {
        FractalDeviceModel.AxeFxIII => "axe-fx-iii",
        FractalDeviceModel.FM3 => "fm3",
        FractalDeviceModel.FM9 => "fm9",
        _ => throw new ArgumentOutOfRangeException(nameof(model)),
    };

    public static FractalDeviceModel ParseModel(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized switch
        {
            "axefx3" or "axefxiii" => FractalDeviceModel.AxeFxIII,
            "fm3" => FractalDeviceModel.FM3,
            "fm9" => FractalDeviceModel.FM9,
            _ => throw new ArgumentException("Model must be one of: axe-fx-iii, fm3, fm9.", nameof(value)),
        };
    }
}

