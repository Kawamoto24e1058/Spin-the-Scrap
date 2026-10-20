namespace SpinTheScrap.Interaction
{
    /// <summary>見て E で使える設置物（スロット台・エレベーター）。</summary>
    public interface IInteractable
    {
        /// <summary>照準を合わせたときの案内文（文字列テーブルで引いた後の文）。</summary>
        string Prompt(Core.StringTable strings);
        void Interact(Interactor who);
    }
}
