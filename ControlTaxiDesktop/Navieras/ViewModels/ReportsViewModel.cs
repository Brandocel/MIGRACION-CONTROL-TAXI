using System.Collections.ObjectModel;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.ViewModels;

public sealed class ReportsViewModel : NavierasViewModelBase
{
    private DateOnly _fromDate = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly _toDate = DateOnly.FromDateTime(DateTime.Today);
    private string _passengerGrouping = "service";
    private string _reportStatus = string.Empty;
    private string _exportPreviewText = string.Empty;

    public DateOnly FromDate { get => _fromDate; set => SetField(ref _fromDate, value); }
    public DateOnly ToDate { get => _toDate; set => SetField(ref _toDate, value); }
    public string PassengerGrouping { get => _passengerGrouping; set => SetField(ref _passengerGrouping, value); }
    public string ReportStatus { get => _reportStatus; set => SetField(ref _reportStatus, value); }
    public string ExportPreviewText { get => _exportPreviewText; set => SetField(ref _exportPreviewText, value); }

    public ObservableCollection<NavierasDailyReportRow> DailyReports { get; } = [];
    public ObservableCollection<NavierasCompanyReportItem> CompanyReports { get; } = [];
    public ObservableCollection<NavierasBoatReportItem> ShipReports { get; } = [];
    public ObservableCollection<NavierasPersonnelReportRow> GuideReports { get; } = [];
    public ObservableCollection<NavierasPersonnelReportRow> CaptainReports { get; } = [];
    public ObservableCollection<NavierasPassengerReportRow> PassengerReports { get; } = [];
    public ObservableCollection<NavierasPunctualitySummaryRow> PunctualityReports { get; } = [];
    public ObservableCollection<NavierasBraceletReportRow> BraceletReports { get; } = [];
}
