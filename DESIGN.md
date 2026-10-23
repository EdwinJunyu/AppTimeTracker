---
version: alpha
name: AppTimeTracker
description: 中文 Windows 桌面计时器，沿用珍珠白磨砂表面与浅冰蓝选择态。
colors:
  primary: "#4169D4"
  foreground: "#24344B"
  muted: "#56677C"
  background: "#F7F9FC"
  border: "#DEE6F1"
  success: "#2C6954"
typography:
  body:
    fontFamily: "Microsoft YaHei UI"
  heading:
    fontFamily: "Microsoft YaHei UI"
  data:
    fontFamily: "Consolas"
rounded:
  button: "10px"
  surface: "18px"
spacing:
  surface-padding: "12px"
  form-padding: "24px"
components:
  button: {}
  surface: {}
  table: {}
  note-editor: {}
---

# 软件时钟界面约定

## Overview

面向需要回看软件使用时长及工作内容的 Windows 桌面用户。以桌面日志工具的清晰表格为中心，固定浅色的珍珠白表面与冰蓝选中态是现有视觉标识。备注功能延伸记录内容，不改变计时或导航的既有结构。

依据为 [README.md](README.md) 的界面及计时规则、现有 `Ui` 共享控件与已确认的备注需求。界面语言为简体中文；未声明特定国家业务规则，也不推断日本市场要求。保留 Windows 原生交互、系统字体和高对比支持；不用营销大标题、装饰图形或新的主题。

运行时为唯一 token 所有者（Model B）：`AppTimeTracker/Dialogs.cs` 的 `Ui` → 共享控件 → 主界面、历史及备注编辑器；本文件镜像这些值并说明用途，不生成另一套颜色或样式。

## Colors

`primary` 对应 `Ui.Accent`，用于主操作与当前视图；`foreground`、`muted`、`background`、`border`、`success` 分别对应 `Ui.Ink`、`MutedInk`、`Paper`、`Line`、`Success`。表格正文保持白色，选中态由 `Ui.Grid` 统一绘制。高对比模式使用 `SystemColors`，不能以自定义 token 覆盖系统选择。错误必须同时显示文字。

## Typography

`Ui.Body` 为 Microsoft YaHei UI 10pt，`Ui.Title` 默认使用同字体 24pt；时长列沿用 `Ui.Data` 的 Consolas 10pt 和右对齐。前置 token 仅记录字体族，保留运行时以 point 为单位的字号，避免引入第二套像素换算值。备注是中文正文，不使用数字字体。标题和说明按字体首选高度布局，并随系统 DPI 缩放。完整长备注通过可滚动多行编辑器访问，不只依赖悬停提示。

## Layout

沿用 `TableLayoutPanel` 与可缩放窗体。主界面软件列表增加备注信息及明确编辑入口，历史保持明细、每日、累计三个视图。表格拥有内部原生滚动条；历史保持每页 50 行及现有分页。备注编辑器保留软件与计时开始时间作为上下文，多行输入占据剩余空间，操作栏及错误区域有固定留白，避免保存反馈挤动按钮。

## Elevation & Depth

`Ui.Style` 的浅色渐变背景与 `Ui.Surface` 的磨砂圆角是现有层次。正文不透视桌面，不添加 3D 装饰或新阴影。Windows 标题栏材料由 `WindowsIntegration.ApplyWindowMaterial` 管理，旧系统保持回退。

## Shapes

按钮圆角由 `Ui.GlassButton` 的 10 个逻辑像素定义，默认面板由 `Ui.Surface` 的 18 个逻辑像素定义，均按 DPI 缩放。原生输入框、下拉菜单、日期选择器与滚动条保留 Windows 外观和交互。

## Components

| 本文件 token / 组件 | 运行时所有者 | 消费者 |
|---|---|---|
| colors | `Ui` 静态颜色字段 | `Ui.Style`、按钮、表格、文字 |
| typography | `Ui.Body`、`Ui.Title`、`Ui.Data` | 标题、输入、表格、时长 |
| rounded.button | `Ui.GlassButton.OnPaint` | `Ui.Button`、`Ui.PrimaryButton` |
| rounded.surface、spacing.surface-padding | `Ui.Surface` | 各窗体表面 |
| spacing.form-padding | `AppEditor`、`HistoryForm` 布局 | 对话框布局 |
| note-editor | `SessionNoteEditor`，复用 `Ui.Style` / `Surface` / `Button` | 主页及历史明细 |

新备注编辑器沿用现有软件编辑器的“保存 / 取消”、内联错误及原生多行 `TextBox`。主页与历史使用同一编辑器，保持字段命名、保存行为和失败恢复一致。行为细节见 [UX-CONTRACT.md](UX-CONTRACT.md)。

## Do's and Don'ts

- 保持备注与计时记录的对应关系，清楚区分当前计时和最近一次已结束计时。
- 保留可见焦点、原生键盘操作、中文输入法及原生滚动条；表格之外提供明确编辑按钮。
- 不改变现有配色、计时规则、托盘流程或日期范围语义。
- 不因备注为空弹窗催填，不把多次计时的备注拼接成一条。

当前文档来自代码扫描。Linux 可验证编译和数据逻辑，Windows 的实际布局、DPI、高对比、焦点及中文输入法需要原生运行验证；静态检查不能替代该验证。
