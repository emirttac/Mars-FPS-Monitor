using System.Windows;

namespace FPSOverlay
{
    public partial class ControlPanelWindow
    {
        private void BtnFanBetaInfo_Click(object sender, RoutedEventArgs e)
        {
            MarsInfoDialog.ShowInfo(
                this,
                _s.FanBetaInfoTitle,
                _s.FanBetaInfoSubtitle,
                _s.FanBetaInfoBody,
                _s.FanBetaInfoComingTitle,
                _s.FanBetaInfoComingBody,
                _s.FanBetaInfoOk);
        }
    }
}
