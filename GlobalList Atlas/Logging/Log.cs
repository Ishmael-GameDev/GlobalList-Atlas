namespace GlobalListAtlas.Logging;

public static class Log
{
    private const string Prefix = "[GlobalListAtlas] ";

    public static void Info(string message) => Modding.Logger.Log(Prefix + message);

    public static void Warn(string message) => Modding.Logger.LogWarn(Prefix + message);

    public static void Error(string message) => Modding.Logger.LogError(Prefix + message);

    public static void Error(string message, System.Exception e) =>
        Modding.Logger.LogError(Prefix + message + " | " + e);
}
