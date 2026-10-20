using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace BorderlessFlatWindow.Presentation;

internal sealed class SceneCard(Action invoke) : Border
{
    protected override AutomationPeer OnCreateAutomationPeer() => new CardPeer(this, invoke);
    private sealed class CardPeer(SceneCard owner, Action invoke) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override string GetClassNameCore() => "SpatialCard";
        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPattern(pattern);
        public void Invoke() => owner.Dispatcher.BeginInvoke(invoke);
    }
}
