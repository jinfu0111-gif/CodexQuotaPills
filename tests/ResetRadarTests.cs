using System;
using System.Globalization;
using CodexUsageOverlay;

internal static class ResetRadarTests
{
    private static int failures;

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--live-auto-resume-readonly")
            return AutoResumeTests.LiveReadOnly();
        if (args.Length == 1 && args[0] == "--live-auto-resume-scan-readonly")
            return AutoResumeTests.LiveScanReadOnly();
        Run("auto-resume requires structured quota failure", AutoResumeTests.StructuredErrorsOnly);
        Run("auto-resume respects attention and settings", AutoResumeTests.AttentionAndSettingsGuard);
        Run("auto-resume requires complete canonical history", AutoResumeTests.CanonicalHistoryMustBeComplete);
        Run("auto-resume rechecks real quota and weekly limit", AutoResumeTests.QuotaRecheckedAndWeeklyHonored);
        Run("auto-resume intent survives restart and cancel", AutoResumeTests.IntentSurvivesRestartAndCancellation);
        Run("future app versions require compatible verified contract", AutoResumeTests.FutureVersionsRequireVerifiedContract);
        Run("desktop contract archive bounds are validated", AutoResumeTests.ArchiveBoundsAreValidated);
        Run("auto-resume isolated IPC dispatch preserves owner and cancel", AutoResumeTests.IsolatedDispatchUsesOriginalOwnerAndCancel);
        Run("auto-resume defaults migrate once and preserve pause", AutoResumeTests.AutomaticDefaultsMigrateOnceAndPreservePause);
        Run("background scan and early recovery need no clicks", AutoResumeTests.BackgroundScanAndEarlyRecoveryNeedNoClicks);
        if (args.Length == 1 &&
            (args[0] == "--live-msix-check" || args[0] == "--live-msix-url"))
        {
            Blues19.CodexInstaller.Logger log = new Blues19.CodexInstaller.Logger(false);
            Blues19.CodexInstaller.InstalledPackage installed =
                Blues19.CodexInstaller.AppxInstaller.GetInstalled(log);
            Console.WriteLine("Installed=" + (installed == null ? "not-found" : installed.Version.ToString()));
            Blues19.CodexInstaller.PackageInfo latest =
                Blues19.CodexInstaller.StoreApi.FindLatestPackage(log,
                    System.Threading.CancellationToken.None);
            Console.WriteLine("Latest=" + latest.VersionText);
            Console.WriteLine("Size=" + latest.SizeBytes);
            if (args[0] == "--live-msix-url")
            {
                Blues19.CodexInstaller.StoreApi.PrepareDownload(latest, log,
                    System.Threading.CancellationToken.None);
                Console.WriteLine("DownloadHost=" + new Uri(latest.Url).Host);
            }
            return 0;
        }
        Run("completed reset is today", CompletedResetIsToday);
        Run("future schedule today is pending", FutureScheduleTodayIsPending);
        Run("expired exact schedule is not today", ExpiredExactScheduleIsNotToday);
        Run("scheduled date range crosses Shanghai local day", DateRangeCrossesShanghaiLocalDay);
        Run("Pacific date range honors daylight saving transition", DateRangeHonorsDaylightSavingTransition);
        Run("exactly thirty hours is still fresh", ExactlyThirtyHoursIsFresh);
        Run("over thirty hours is offline", OverThirtyHoursIsOffline);
        Run("future feed timestamp is rejected", FutureFeedTimestampIsRejected);
        Run("bare local timestamp is rejected", BareTimestampIsRejected);
        Run("wrong source host is rejected", WrongSourceHostIsRejected);
        Run("operator records do not hide or impersonate Tibo", OperatorRecordsDoNotHideTibo);
        Run("reset bank completion rationale is accepted", ResetBankCompletionRationaleIsAccepted);
        Run("confidence and countdown are displayed", ConfidenceAndCountdownAreDisplayed);
        Run("completed banner expires at local midnight", CompletedBannerExpiresAtLocalMidnight);
        Run("cached radar is not shown as live", CachedRadarIsNotShownAsLive);
        Run("transient network failure preserves fresh radar", TransientNetworkFailurePreservesFreshRadar);
        Run("stale network failure becomes offline", StaleNetworkFailureBecomesOffline);
        Run("scheduled headline uses reset time", ScheduledHeadlineUsesResetTime);
        Run("completed reset overrides active schedule", CompletedResetOverridesActiveSchedule);
        Run("completed schedule stays cleared after local midnight", CompletedScheduleStaysClearedAfterLocalMidnight);
        Run("layered bitmap uses logical DPI", RenderingCompatibilityTests.LayeredBitmapUsesLogicalDpi);
        Run("unsafe font falls back to text font", RenderingCompatibilityTests.UnsafeFontFallsBackToTextFont);
        Run("text renders at mixed DPI scale", RenderingCompatibilityTests.TextRendersAtMixedDpiScale);
        Run("update menu uses readable rainbow palette", UpdateMenuVisualsTests.UpdateMenuUsesReadableRainbowPalette);
        Run("rainbow menu separates update and exit actions", UpdateMenuVisualsTests.RainbowMenuSeparatesUpdateAndExitActions);
        Run("menu pills center within the whole card", UpdateMenuVisualsTests.PillIsCenteredInWholeMenu);
        Run("main usage does not own mouse input", OverlayInteractionTests.MainUsageIsNotInteractive);
        Run("radar status click opens Runway", OverlayInteractionTests.RadarStatusClickOpensRunway);
        Run("missing overlay recovers while Codex runs", CodexLifecycleTests.MissingOverlayRecoversWhileCodexRuns);
        Run("lifecycle start failures are throttled", CodexLifecycleTests.FailedStartsAreThrottled);
        Run("missing Codex uses a shutdown grace period", CodexLifecycleTests.MissingCodexUsesGracePeriod);
        Run("composer pills expose native quota fields", ComposerUsagePillsTests.NativeQuotaFieldsAreExposed);
        Run("composer pills omit unavailable quota fields", ComposerUsagePillsTests.UnavailableQuotaFieldsAreOmitted);
        Run("composer pills clamp invalid percentages", ComposerUsagePillsTests.PercentagesAreClamped);
        Run("quota hover shows reset times", ComposerUsagePillsTests.QuotaHoverShowsResetTimes);
        Run("combined hover card preserves credits and live Tibo", ComposerUsagePillsTests.PlanHoverPrefersCreditsThenTibo);
        Run("PLUS click target has forgiving edges", ComposerUsagePillsTests.PlanClickTargetHasForgivingEdges);
        Run("pink credit hover lists each individual expiry", ComposerUsagePillsTests.CreditsHoverListsEachExpiry);
        Run("credit hover handles partial legacy and zero inventory", ComposerUsagePillsTests.CreditsHoverHandlesMissingAndEmptyInventory);
        Run("narrow layout preserves credits before tokens", ComposerUsagePillsTests.NarrowLayoutPreservesCreditsBeforeTokens);
        Run("long usage status keeps the Token value visible", UsageDisplayTextTests.LongRateLimitStatusIsLocalizedAndTokenIsKept);
        Run("wide usage layout keeps detailed labels", UsageDisplayTextTests.WideLayoutKeepsDetailedLabels);
        Run("narrow usage layout prioritizes Token", UsageDisplayTextTests.NarrowLayoutPrioritizesToken);
        Run("account and quota are required for trusted usage", UsageTrustPolicyTests.AccountAndQuotaAreRequiredForTrustedSnapshot);
        Run("ChatGPT nullable email and real Free window are accepted", UsageTrustPolicyTests.ChatgptWithNullEmailAndFreeWindowIsAccepted);
        Run("non-ChatGPT identity is rejected", UsageTrustPolicyTests.NonChatgptIdentityIsRejected);
        Run("quota plan overrides account plan", UsageTrustPolicyTests.QuotaPlanOverridesAccountPlan);
        Run("valid window plan overrides account fallback", UsageTrustPolicyTests.ValidWindowPlanOverridesAccountFallback);
        Run("invalid window plan does not leak", UsageTrustPolicyTests.InvalidWindowPlanDoesNotLeakIntoSnapshot);
        Run("quota window requires duration", UsageTrustPolicyTests.WindowWithoutDurationIsRejected);
        Run("weekly partial response preserves cached fields", UsageTrustPolicyTests.PartialWeeklyWindowPreservesCachedFields);
        Run("explicit zero reset credits override cache", UsageTrustPolicyTests.ExplicitZeroCreditsOverridesCachedCount);
        Run("reset credit expiry is parsed", UsageTrustPolicyTests.ResetCreditExpiryIsParsed);
        Run("available credit inventory preserves status and duplicate dates", UsageTrustPolicyTests.AvailableCreditInventoryIsPreserved);
        Run("credit inventory merge clears stale dates and copies snapshots", UsageTrustPolicyTests.CreditInventoryMergeDoesNotLeakOrRetainStaleDates);
        Run("individual credit expiry cache round trips", UsageTrustPolicyTests.CreditExpiryCacheRoundTripsWithoutInventingDates);
        Run("equal live values restore presence flags", UsageTrustPolicyTests.EqualLiveValuesRestorePresenceFlags);
        Run("valid Pro snapshot repairs cached Free", UsageTrustPolicyTests.ValidProSnapshotRepairsCachedFree);
        Run("rejected usage snapshot leaves cache untouched", UsageTrustPolicyTests.RejectedSnapshotLeavesCacheUntouched);
        Run("manual update check only bypasses time throttle", GitHubReleaseUpdateTests.ManualCheckBypassesOnlyTimeThrottle);
        Run("GitHub releases hashes and archive paths are validated", GitHubReleaseUpdateTests.ReleasesAndArchivesAreValidated);
        Run("new radar banner expires and cannot replay", ResetRadarBannerTests.NewInformationExpiresAndCannotReplay);
        Run("radar pill lines align and circular close is centered", ResetRadarBannerTests.PillLayoutAlignsTextAndCentersClose);
        Run("first-run guide migration preserves existing users", OverlaySettingsTests.FirstRunGuideMigrationPreservesExistingUsers);
        Run("current guide replaces obsolete controls and replays once", OverlaySettingsTests.CurrentGuideReplacesObsoleteControlsAndReplaysOnce);
        Run("guide bubble follows anchor and stays on screen", OverlaySettingsTests.GuideBubbleFollowsAnchorAndStaysOnScreen);
        Run("completed guide survives an older settings draft", OverlaySettingsTests.CompletedGuideSurvivesAnOlderSettingsDraft);
        Run("independent settings uses monotonic guide state", OverlaySettingsTests.IndependentSettingsUsesMonotonicGuideState);
        Run("placement persists and maps to three anchors", OverlaySettingsTests.PlacementRoundTripsAndCalculatesThreePositions);
        Run("unified PLUS menu keeps all seven pills separate", PlacementMenuLayoutTests.UnifiedMenuActionsUseSeparatePills);
        Run("dialogs center above PLUS", PopupAnchorPlacementTests.CentersDialogsAbovePlusWhenSpaceAllows);
        Run("nearly fitting updater stays above PLUS", PopupAnchorPlacementTests.KeepsNearlyFittingUpdaterAbovePlus);
        Run("dialogs fall below only when above cannot fit", PopupAnchorPlacementTests.FallsBelowOnlyWhenAboveCannotFit);
        Run("MSIX updater targets official Codex package", MsixUpdaterTests.TargetsOfficialPackage);
        Run("MSIX updater rejects unverifiable cache", MsixUpdaterTests.RejectsUnverifiableCache);
        Run("MSIX updater state is isolated", MsixUpdaterTests.KeepsUpdaterStateSeparate);
        Run("MSIX updater migrates verified cache safely", MsixUpdaterTests.MigratesVerifiedCacheWithoutRemovingOriginal);

        Console.WriteLine(failures == 0 ? "All reset radar tests passed." : failures + " reset radar test(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void CompletedResetIsToday()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-28T12:30:00Z");
        ResetRadarData data = Parse(Feed(
            "2026-07-28T12:30:00Z",
            "2026-07-28T12:30:00Z",
            CompletedEvent("2026-07-28T12:00:00Z", "1001")), now);
        Assert(data.Status == ResetRadarStatus.CompletedToday, data.Status.ToString());
        Assert(data.SourceUrl.EndsWith("/1001", StringComparison.Ordinal), data.SourceUrl);
    }

    private static void FutureScheduleTodayIsPending()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        ResetRadarData data = Parse(Feed(
            "2026-07-28T12:00:00Z",
            "2026-07-28T12:00:00Z",
            ScheduledEvent("2026-07-28T11:00:00Z", "2026-07-28T13:00:00Z", "1002")), now);
        Assert(data.Status == ResetRadarStatus.ScheduledToday, data.Status.ToString());
    }

    private static void ConfidenceAndCountdownAreDisplayed()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-10T02:02:27Z");
        ResetRadarData data = Parse(Feed(
            "2026-08-10T02:02:27Z",
            "2026-08-10T02:02:27Z",
            ScheduledEvent("2026-08-08T20:34:50Z", "2026-08-10T07:00:00Z", "1007")), now);
        Assert(data.Confidence.HasValue && Math.Abs(data.Confidence.Value - 0.95d) < 0.0001d,
            data.Confidence.HasValue ? data.Confidence.Value.ToString() : "missing confidence");
        Assert(ResetRadarDisplay.ConfidenceSuffix(data) == " · 置信度 95%",
            ResetRadarDisplay.ConfidenceSuffix(data));
        CultureInfo culture = CultureInfo.GetCultureInfo("zh-CN");
        string localStart = data.EffectiveAt.Value.ToLocalTime().ToString("M月d日 HH:mm", culture);
        string localEnd = data.EffectiveUntil.Value.ToLocalTime().ToString("M月d日 HH:mm", culture);
        string expected = "计划重置：" + localStart + "—" + localEnd +
            " · 4小时57分33秒后—28小时56分33秒后";
        Assert(ResetRadarDisplay.BuildPrimaryLine(data, now) ==
            expected,
            ResetRadarDisplay.BuildPrimaryLine(data, now));
    }

    private static void CompletedBannerExpiresAtLocalMidnight()
    {
        DateTimeOffset localOccurrence = new DateTimeOffset(
            new DateTime(2026, 8, 10, 23, 59, 30),
            TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 8, 10, 23, 59, 30)));
        ResetRadarData data = new ResetRadarData
        {
            Status = ResetRadarStatus.CompletedToday,
            AnnouncedAt = localOccurrence,
            SourceUrl = "https://x.com/thsottiaux/status/1008",
            NetworkAvailable = true
        };
        Assert(ResetRadarDisplay.ShouldShow(data, localOccurrence), "completion hidden before midnight");
        Assert(!ResetRadarDisplay.ShouldShow(data, localOccurrence.AddMinutes(1)),
            "completion remained visible after midnight");
    }

    private static void CachedRadarIsNotShownAsLive()
    {
        ResetRadarData data = new ResetRadarData
        {
            Status = ResetRadarStatus.ScheduledToday,
            EffectiveAt = DateTimeOffset.Now.AddHours(1),
            EffectiveUntil = DateTimeOffset.Now.AddHours(2),
            SourceUrl = "https://x.com/thsottiaux/status/1009",
            NetworkAvailable = false,
            IsFromCache = true
        };
        Assert(!ResetRadarDisplay.ShouldShow(data, DateTimeOffset.Now), "cached event was shown as live");
    }

    private static void TransientNetworkFailurePreservesFreshRadar()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        ResetRadarData previous = Parse(Feed(
            "2026-07-28T12:00:00Z",
            "2026-07-28T11:00:00Z",
            ScheduledEvent("2026-07-28T10:00:00Z", "2026-07-28T13:00:00Z", "1014")), now);
        ResetRadarData failed = ResetRadarParser.WithNetworkFailure(previous, "连接超时", now.AddMinutes(1));
        Assert(failed.Status == ResetRadarStatus.ScheduledToday, failed.Status.ToString());
        Assert(failed.StatusLabel == "今日有预告", failed.StatusLabel);
        Assert(failed.RefreshPending, "refresh retry state was not retained");
        Assert(failed.IsFromCache && !failed.NetworkAvailable, "fresh data was not marked as cached retry");
        Assert(ResetRadarDisplay.BuildHeadline(failed, now).EndsWith(" · 网络重试中", StringComparison.Ordinal),
            ResetRadarDisplay.BuildHeadline(failed, now));
    }

    private static void StaleNetworkFailureBecomesOffline()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        ResetRadarData previous = Parse(Feed(
            "2026-07-28T12:00:00Z",
            "2026-07-27T05:59:59Z",
            String.Empty), now);
        ResetRadarData failed = ResetRadarParser.WithNetworkFailure(previous, "连接超时", now);
        Assert(failed.Status == ResetRadarStatus.Offline, failed.Status.ToString());
        Assert(!failed.RefreshPending, "stale data remained in retry state");
    }

    private static void ScheduledHeadlineUsesResetTime()
    {
        DateTime localNowValue = new DateTime(2026, 8, 10, 10, 2, 27, DateTimeKind.Unspecified);
        DateTime localStartValue = new DateTime(2026, 8, 10, 15, 0, 0, DateTimeKind.Unspecified);
        DateTimeOffset localNow = new DateTimeOffset(
            localNowValue,
            TimeZoneInfo.Local.GetUtcOffset(localNowValue));
        DateTimeOffset localStart = new DateTimeOffset(
            localStartValue,
            TimeZoneInfo.Local.GetUtcOffset(localStartValue));
        ResetRadarData data = new ResetRadarData
        {
            Status = ResetRadarStatus.ScheduledToday,
            StatusLabel = "今日有预告",
            EffectiveAt = localStart
        };
        Assert(ResetRadarDisplay.BuildHeadline(data, localNow) == "预计今日15:00后有重置",
            ResetRadarDisplay.BuildHeadline(data, localNow));
        Assert(ResetRadarDisplay.BuildPillLabel(data, localNow) == "15:00后重置",
            ResetRadarDisplay.BuildPillLabel(data, localNow));
        data.EffectiveUntil = localStart.AddHours(24);
        DateTimeOffset activeNow = localStart.AddMinutes(1);
        Assert(ResetRadarDisplay.BuildHeadline(data, activeNow) == "重置时段已开始",
            ResetRadarDisplay.BuildHeadline(data, activeNow));
        Assert(ResetRadarDisplay.BuildPillLabel(data, activeNow) == "重置进行中",
            ResetRadarDisplay.BuildPillLabel(data, activeNow));
    }

    private static void CompletedResetOverridesActiveSchedule()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        string events = ScheduledEvent("2026-08-08T20:34:50Z", "2026-08-10T07:00:00Z", "1010") +
            "," + CompletedEvent("2026-08-10T12:00:00Z", "1011");
        ResetRadarData data = Parse(Feed(
            "2026-08-10T12:00:00Z",
            "2026-08-10T12:00:00Z",
            events), now);
        Assert(data.Status == ResetRadarStatus.CompletedToday, data.Status.ToString());
        Assert(data.EvidencePostId == "1011", data.EvidencePostId);
    }

    private static void CompletedScheduleStaysClearedAfterLocalMidnight()
    {
        DateTimeOffset scheduleStart = DateTimeOffset.Parse("2026-08-10T07:00:00Z");
        DateTime localMidnightValue = scheduleStart.ToLocalTime().Date.AddDays(1);
        DateTimeOffset localMidnight = new DateTimeOffset(
            localMidnightValue,
            TimeZoneInfo.Local.GetUtcOffset(localMidnightValue));
        DateTimeOffset completedAt = localMidnight.AddMinutes(-1);
        DateTimeOffset now = localMidnight.AddMinutes(1);
        string events = ScheduledEvent(
            "2026-08-08T20:34:50Z",
            scheduleStart.ToString("o", CultureInfo.InvariantCulture),
            "1012") + "," + CompletedEvent(
                completedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                "1013");
        ResetRadarData data = Parse(Feed(
            now.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            now.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            events), now);
        Assert(data.Status == ResetRadarStatus.NoSignal, data.Status.ToString());
    }

    private static void ExpiredExactScheduleIsNotToday()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        ResetRadarData data = Parse(Feed(
            "2026-07-28T12:00:00Z",
            "2026-07-28T12:00:00Z",
            ScheduledEvent("2026-07-28T10:00:00Z", "2026-07-28T11:00:00Z", "1003")), now);
        Assert(data.Status == ResetRadarStatus.NoSignal, data.Status.ToString());
    }

    private static void DateRangeCrossesShanghaiLocalDay()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-10T16:00:00Z");
        ResetRadarData data = Parse(Feed(
            "2026-08-10T16:00:00Z",
            "2026-08-10T16:00:00Z",
            ScheduledEvent("2026-08-08T20:34:50Z", "2026-08-10T07:00:00Z", "1004")), now);
        Assert(data.Status == ResetRadarStatus.ScheduledToday, data.Status.ToString());
        Assert(data.EffectiveUntil.HasValue && data.EffectiveUntil.Value == DateTimeOffset.Parse("2026-08-11T06:59:00Z"),
            data.EffectiveUntil.HasValue ? data.EffectiveUntil.Value.ToString("o") : "missing end");
    }

    private static void ExactlyThirtyHoursIsFresh()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        ResetRadarData data = Parse(Feed(
            "2026-07-28T12:00:00Z",
            "2026-07-27T06:00:00Z",
            String.Empty), now);
        Assert(data.Status == ResetRadarStatus.NoSignal, data.Status.ToString());
    }

    private static void DateRangeHonorsDaylightSavingTransition()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-03-08T09:00:00Z");
        ResetRadarData data = Parse(Feed(
            "2026-03-08T09:00:00Z",
            "2026-03-08T09:00:00Z",
            ScheduledEvent("2026-03-07T20:00:00Z", "2026-03-08T08:00:00Z", "1006")), now);
        Assert(data.EffectiveUntil.HasValue && data.EffectiveUntil.Value == DateTimeOffset.Parse("2026-03-09T06:59:00Z"),
            data.EffectiveUntil.HasValue ? data.EffectiveUntil.Value.ToString("o") : "missing end");
    }

    private static void OverThirtyHoursIsOffline()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-28T12:00:01Z");
        ResetRadarData data = Parse(Feed(
            "2026-07-28T12:00:01Z",
            "2026-07-27T06:00:00Z",
            String.Empty), now);
        Assert(data.Status == ResetRadarStatus.Offline, data.Status.ToString());
    }

    private static void BareTimestampIsRejected()
    {
        ResetRadarData data;
        string error;
        string json = Feed("2026-07-28T12:00:00", "2026-07-28T12:00:00Z", String.Empty);
        bool parsed = ResetRadarParser.TryParse(json, DateTimeOffset.Parse("2026-07-28T12:00:00Z"), out data, out error);
        Assert(!parsed, "unexpectedly parsed");
    }

    private static void FutureFeedTimestampIsRejected()
    {
        ResetRadarData data;
        string error;
        string json = Feed("2026-07-28T12:20:01Z", "2026-07-28T12:00:00Z", String.Empty);
        bool parsed = ResetRadarParser.TryParse(json, DateTimeOffset.Parse("2026-07-28T12:00:00Z"), out data, out error);
        Assert(!parsed, "unexpectedly parsed");
    }

    private static void WrongSourceHostIsRejected()
    {
        ResetRadarData data;
        string error;
        string json = Feed(
            "2026-07-28T12:00:00Z",
            "2026-07-28T12:00:00Z",
            CompletedEvent("2026-07-28T11:00:00Z", "1005")).Replace("https://x.com/", "https://example.com/");
        bool parsed = ResetRadarParser.TryParse(json, DateTimeOffset.Parse("2026-07-28T12:00:00Z"), out data, out error);
        Assert(!parsed, "unexpectedly parsed");
    }

    private static void ResetBankCompletionRationaleIsAccepted()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T01:00:00Z");
        ResetRadarData data = Parse(Feed(
            "2026-08-22T01:00:00Z",
            "2026-08-22T01:00:00Z",
            Event("reset_completed", "2026-08-22T00:50:36Z", null, "1015",
                "Explicit Codex reset-bank credit announcement.")), now);
        Assert(data.Status == ResetRadarStatus.CompletedToday, data.Status.ToString());
    }

    private static void OperatorRecordsDoNotHideTibo()
    {
        const string timestamp = "2026-07-28T12:00:00Z";
        const string operatorEvent = "{\"kind\":\"reset_completed\",\"source\":{\"origin\":\"operator\",\"postId\":\"op_123\"}}";
        DateTimeOffset now = DateTimeOffset.Parse(timestamp);
        ResetRadarData data = Parse(Feed(timestamp, timestamp,
            CompletedEvent("2026-07-28T11:00:00Z", "1005") + "," + operatorEvent), now);
        Assert(data.Status == ResetRadarStatus.CompletedToday && data.EvidencePostId == "1005", "operator broke Tibo feed");
        data = Parse(Feed(timestamp, timestamp, operatorEvent), now);
        Assert(data.Status == ResetRadarStatus.NoSignal && !ResetRadarDisplay.ShouldShow(data, now), "operator impersonated Tibo");
    }

    private static ResetRadarData Parse(string json, DateTimeOffset now)
    {
        ResetRadarData data;
        string error;
        if (!ResetRadarParser.TryParse(json, now, out data, out error))
            throw new InvalidOperationException(error);
        return data;
    }

    private static string Feed(string generatedAt, string lastSuccessfulCheckAt, string events)
    {
        return "{\"schemaVersion\":1," +
            "\"generatedAt\":\"" + generatedAt + "\"," +
            "\"lastSuccessfulCheckAt\":\"" + lastSuccessfulCheckAt + "\"," +
            "\"monitor\":{\"status\":\"ok\",\"errorCode\":null}," +
            "\"events\":[" + events + "]}";
    }

    private static string CompletedEvent(string announcedAt, string postId)
    {
        return Event("reset_completed", announcedAt, null, postId,
            "Explicit Codex quota reset announcement.");
    }

    private static string ScheduledEvent(string announcedAt, string effectiveAt, string postId)
    {
        return Event("reset_scheduled", announcedAt, effectiveAt, postId,
            "Explicit Codex quota reset schedule.");
    }

    private static string Event(string kind, string announcedAt, string effectiveAt, string postId, string rationale)
    {
        string effective = effectiveAt == null ? "null" : "\"" + effectiveAt + "\"";
        return "{\"kind\":\"" + kind + "\"," +
            "\"announcedAt\":\"" + announcedAt + "\"," +
            "\"effectiveAt\":" + effective + "," +
            "\"scope\":{\"plans\":[\"all\"],\"windows\":[\"weekly\"]}," +
            "\"source\":{\"handle\":\"thsottiaux\",\"postId\":\"" + postId + "\"," +
            "\"url\":\"https://x.com/thsottiaux/status/" + postId + "\"}," +
            "\"confidence\":0.95,\"rationale\":\"" + rationale + "\"," +
            "\"text\":\"Test reset announcement.\"}";
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("FAIL " + name + ": " + ex.Message);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
