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
            if (Net.NetSession.IsRemoteView)
            {
                var net = Net.NetSession.Instance;
                string netText = s.Format("objective.label", s.Get("objective." + net.ObjectiveKind), net.ObjectiveProgress + "/" + net.ObjectiveTarget);
                return net.ObjectiveTarget > 0 && net.ObjectiveProgress >= net.ObjectiveTarget ? netText + "  " + s.Get("objective.done") : netText;
            }
            var objective = session.Run.Objective;
            string progress = (int)objective.Progress + "/" + (int)objective.Target;
            string text = s.Format("objective.label", s.Get(objective.NameKey), progress);
            return objective.IsComplete ? text + "  " + s.Get("objective.done") : text;
        }
    }
}
