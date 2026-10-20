using SpinTheScrap.Core;
using SpinTheScrap.Session;

namespace SpinTheScrap.UI
{
    /// <summary>攻略条件の表示文（HUD とスロット台のメニューで共通）。</summary>
    public static class ObjectiveText
    {
        public static string Describe(RunSession session)
        {
            var s = session.Strings;
            var objective = session.Run.Objective;
            string progress = objective.Kind == FloorObjectiveKind.PowerGenerator
                ? session.Medals(objective.Progress) + "/" + session.Medals(objective.Target)
                : (int)objective.Progress + "/" + (int)objective.Target;
            string text = s.Format("objective.label", s.Get(objective.NameKey), progress);
            return objective.IsComplete ? text + "  " + s.Get("objective.done") : text;
        }
    }
}
