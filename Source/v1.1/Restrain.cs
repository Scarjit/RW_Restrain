using Verse;

namespace Restrain
{
    [StaticConstructorOnStartup]
    public class Restrain
    {
        public static string Version = "1.1";
        static Restrain()
        {
            Log.Message($"Loaded Restrain [Version {Version}]");
        }
    }
}