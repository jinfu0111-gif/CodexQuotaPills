# 重置信息横幅效果图

这组图片由当前本机 0.2.24 程序的真实绘制函数导出，使用默认字体；横幅与额度胶囊共享浅色背景、边框与文字色。公告内容、日期和置信度是固定示例，不代表真实 Tibo 公告。导出不访问网络、不弹出真实提醒，也不改变已读或自动续跑记录。构建后运行 `tools/export-ui-previews.ps1` 可重新生成。

| 状态 | 原尺寸 | 清晰版（200% DPI） |
| --- | --- | --- |
| 今日已重置 | [查看](images/reset-radar-banner-preview.png) | [查看](images/reset-radar-banner-preview@2x.png) |
| 今日重置预告 | [查看](images/reset-radar-banner-scheduled-preview.png) | [查看](images/reset-radar-banner-scheduled-preview@2x.png) |
| 鼠标悬停关闭按钮 | [查看](images/reset-radar-banner-close-preview.png) | [查看](images/reset-radar-banner-close-preview@2x.png) |

原尺寸为 450 × 48 像素，清晰版为 900 × 96 像素。实际横幅宽度还会根据胶囊宽度收窄，过长文字使用省略号。两行文字共用左边缘和可用宽度，颜色状态仅由绿点/橙点区分；右侧关闭按钮为圆形，圆心对齐横幅高度中线，× 始终显示，悬停呈浅粉底与红色 ×。运行中仅在新公告出现时显示，10 秒自动关闭，也可手动关闭。

![今日已重置](images/reset-radar-banner-preview.png)

![今日重置预告](images/reset-radar-banner-scheduled-preview.png)

![悬停关闭按钮](images/reset-radar-banner-close-preview.png)

绘制对应 [ResetRadarBannerForm.cs](../ResetRadarBannerForm.cs) 的 `BuildRenderedBitmap`，共享颜色、两行布局、圆形关闭按钮和点击区域对应 [ResetRadarBannerVisuals.cs](../ResetRadarBannerVisuals.cs)。标题与详情文案由 [ResetRadarService.cs](../ResetRadarService.cs) 的 `ResetRadarDisplay` 生成；展示时机由 [ResetRadarBannerPolicy.cs](../ResetRadarBannerPolicy.cs) 控制。

上述外观与点击区域已用于本机 0.2.24，提醒时机和防重播保持。效果图及本说明随独立本机包放在 `dist/CodexQuotaPills-0.2.24-portable/docs/`；旧版本保留，未同步 GitHub。
