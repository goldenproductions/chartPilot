namespace ChartPilot.Core.Checks;

/// <summary>
/// An optional second interface for rules that only make sense for some charts.
/// <para>
/// The engine reports a rule that produced no findings as a <see cref="PassedCheck"/>. That is only
/// meaningful when the rule actually had something to look at: telling a chart with no Istio
/// resources that it "passed" the strict-mTLS check would be a lie. A rule that implements this
/// interface is skipped entirely — neither failed nor passed — when it is not applicable.
/// </para>
/// <para>Rules that do not implement this interface are always applicable.</para>
/// </summary>
public interface IConditionalCheck
{
    bool IsApplicable(CheckContext context);
}
