# Dell Fan Controller - Multi-Server Edition

基于 [cw1997/dell_fans_controller](https://github.com/cw1997/dell_fans_controller) 改进的多服务器风扇调速工具。

## 功能特点

- **多标签管理** — 同时管理多台 Dell 服务器的风扇转速，每个标签独立配置
- **曲线调速** — 可视化温度-转速曲线编辑器，自由拖拽调节挡位
- **传感器选择** — 支持选择特定温度传感器作为调速依据
- **双传感器最高值** — 可选两个传感器中的最高温度进行控制
- **温升紧急保护** — 检测温度骤升，超过设定值立即拉满风扇
- **防抽风策略** — 步进控制 + 动态步进，避免频繁调速
- **调试日志** — 实时显示调速日志，方便排查问题
- **预设方案** — 保存/加载多套调速曲线方案
- **配置文件持久化** — 自动保存服务器配置和曲线参数

## 使用前提

1. Windows 操作系统（需要 .NET Framework 4.0+）
2. 需要管理权限（访问 BMC 需要）
3. 目标服务器需启用 IPMI over LAN

## 快速开始

1. 下载 `bin\NewBuild\DellFanController.exe`
2. 双击运行（会请求管理员权限）
3. 输入 iDRAC/BMC 的 IP、用户名、密码
4. 点击「测试连接」确认连通性
5. 点击「刷新传感器」查看传感器数据
6. 调整曲线后点击「启用自动温控」

## 构建方法

### 环境要求
- Visual Studio 2010+ 或 MSBuild（.NET Framework 4.0 SDK）
- .NET Framework 4.0

### 构建步骤

```bash
# 使用 MSBuild 构建
cd DellFanController
"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe" DellFanController.csproj /p:Configuration=Release /p:Platform=AnyCPU
```

构建完成后，将 `Dell\SysMgt\bmc\` 目录（ipmitool 及其依赖）复制到输出目录。

## 目录结构

```
DellFanController/
├── DellFanController.csproj    # 项目文件
├── FormMain.cs                  # 主界面逻辑
├── FormMain.Designer.cs         # 界面设计器
├── IpmiHelper.cs                # IPMI 命令执行封装
├── ConfigManager.cs             # 配置管理（JSON）
├── Program.cs                   # 程序入口
├── app.manifest                 # 管理员权限清单
├── Dell/
│   └── SysMgt/bmc/
│       ├── ipmitool.exe         # IPMI 工具
│       └── *.dll                # 运行时依赖
└── bin/NewBuild/
    └── DellFanController.exe    # 编译输出
```

## 许可证

本项目基于 [cw1997/dell_fans_controller](https://github.com/cw1997/dell_fans_controller) 开发，遵循原项目许可证。

## 致谢

- 原始项目: [cw1997/dell_fans_controller](https://github.com/cw1997/dell_fans_controller)
- IPMI 工具: ipmitool 项目
- Dell System Management: Dell 官方 BMC 工具
## 已知问题

- **火绒安全软件误报** — 本工具使用 ipmitool.exe 读取 BMC 传感器数据和设置风扇转速，这些属于底层硬件操作，可能被火绒等安全软件识别为可疑行为。如遇拦截，请将程序所在目录加入火绒白名单。
> 本软件全程使用 Deepseek v4 pro 编写。