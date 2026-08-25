namespace GlobalListAtlas.Logging;

// Единая точка логирования
public static class Log
{
    private const string Prefix = "[GlobalListAtlas] ";

    public static void Info(string message)
    {
        Modding.Logger.Log(Prefix + message);
    }

    public static void Warn(string message)
    {
        Modding.Logger.Log(Prefix + "[WARN] " + message);
    }

    public static void Error(string message)
    {
        Modding.Logger.Log(Prefix + "[ERROR] " + message);
    }

    public static void Error(string message, System.Exception e)
    {
        Modding.Logger.Log(Prefix + "[ERROR] " + message + " | " + e);
    }
}