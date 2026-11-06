using System;

namespace CodexUsageOverlay
{
    internal sealed class ResetCreditRecord
    {
        public string Id;
        public string ResetType;
        public string Status;
        public long? ExpiresAt;

        internal ResetCreditRecord Clone() { return (ResetCreditRecord)MemberwiseClone(); }
    }

    internal sealed class ResetCreditInventory
    {
        public string AccountHash;
        public DateTime ReadUtc;
        public int AvailableCount;
        public bool HasDetails;
        public ResetCreditRecord[] Credits = new ResetCreditRecord[0];
    }
}
