using RimWorld;
using Verse;

namespace SRA
{
    public class CompProperties_RemoteMapMonitor : CompProperties
    {
        public string selectTargetLabelKey = "SRA_RemoteMonitoring_SelectTargetLabel";
        public string selectTargetDescKey = "SRA_RemoteMonitoring_SelectTargetDesc";
        public string openTargetLabelKey = "SRA_RemoteMonitoring_OpenTargetLabel";
        public string openTargetDescKey = "SRA_RemoteMonitoring_OpenTargetDesc";
        public string disconnectTargetLabelKey = "SRA_RemoteMonitoring_DisconnectTargetLabel";
        public string disconnectTargetDescKey = "SRA_RemoteMonitoring_DisconnectTargetDesc";
        public string targetSelectionPromptKey = "SRA_RemoteMonitoring_TargetSelectionPrompt";
        public string noTargetMessageKey = "SRA_RemoteMonitoring_NoTargetMessage";
        public string invalidTargetMessageKey = "SRA_RemoteMonitoring_InvalidTargetMessage";
        public string invalidSelectionMessageKey = "SRA_RemoteMonitoring_InvalidSelectionMessage";
        public string linkEstablishedMessageKey = "SRA_RemoteMonitoring_LinkEstablishedMessage";
        public string linkDisconnectedMessageKey = "SRA_RemoteMonitoring_LinkDisconnectedMessage";
        public string openFailedMessageKey = "SRA_RemoteMonitoring_OpenFailedMessage";
        public string inspectStringKey = "SRA_RemoteMonitoring_InspectString";
        public string researchRequiredMessageKey = "SRA_RemoteMonitoring_ResearchRequiredMessage";
        public string selectIconPath = "SRA/UI/Commands/UI_SRA_RemoteMonitoring";
        public string openIconPath = "SRA/UI/Commands/UI_SRA_RemoteMonitoring";
        public string disconnectIconPath = "SRA/UI/Commands/UI_SRA_RemoteMonitoringClose";
        public ResearchProjectDef requiredResearch;
        public bool requirePower = true;
        public bool allowWorldTargetSelection = true;
        public bool allowDisconnect = true;
        public bool jumpToMapAfterOpen = true;
        public bool keepMapAliveWhenLinked = true;

        public CompProperties_RemoteMapMonitor()
        {
            compClass = typeof(CompRemoteMapMonitor);
        }
    }
}
