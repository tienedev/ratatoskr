using System.Reflection;
using BepInEx;
using ValheimRcon;

namespace Ratatoskr
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(ValheimRcon.Plugin.Guid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dev.tiene.ratatoskr";
        public const string Name = "Ratatoskr";
        public const string Version = "0.2.0";

        private float _nextLearn;

        private void Awake()
        {
            var language = Config.Bind("General", "Language", "English",
                "Language for item names in replies (a column of the game's localization, e.g. French, German). " +
                "Queries match this language, English and prefab names.");
            Translations.Load(language.Value);
            PlayerNames.Load();
            RconCommandsUtil.RegisterAllCommands(Assembly.GetExecutingAssembly());
        }

        private float _nextTick;
        private bool _tickFailed;

        private void Update()
        {
            var now = UnityEngine.Time.time;
            if (now >= _nextTick)
            {
                _nextTick = now + 2f;
                try
                {
                    Tracker.Tick();
                    _tickFailed = false;
                }
                catch (System.Exception e)
                {
                    if (!_tickFailed) Logger.LogError(e);
                    _tickFailed = true;
                }
            }
            if (now < _nextLearn) return;
            _nextLearn = now + 60f;
            PlayerNames.LearnOnline();
        }

        private void OnDestroy() => Tracker.Shutdown();
    }
}
