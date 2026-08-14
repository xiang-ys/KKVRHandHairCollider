# KKVR 头发与服装互动

[English](README.md) | **简体中文** | [日本語](README.ja.md)

这是一个面向原版 `KoikatuVR.exe` 的 BepInEx 插件。它把 Quest/SteamVR
控制器接入游戏和已安装 Mod **已经提供的可动物理部分**，包括头发、物理
配饰、裙子与服装 DynamicBone 链，以及已经使用 Unity Cloth 的服装。

插件不会替换 DynamicBone 或 Unity Cloth，也不会为静态模型凭空生成物理。
它的目标是尽可能让游戏和 Mod 中原本能动的部分参与控制器互动，同时保留
原作者设置的骨骼、蒙皮和物理解算方式。

当前版本为 **1.0**。已经通过 39 项自动化测试、Release 构建、程序集检查、
原版 VR 启动测试，以及原版和 Mod 服装的人工游戏内测试。

- [下载最新 GitHub Release](https://github.com/xiang-ys/KKVRHandHairCollider/releases/latest)
- [版本记录](CHANGELOG.md)
- [详细验证记录](VALIDATION.md)
- [当前版本 SHA-256](SHA256SUMS.txt)

本项目是非官方社区项目，与 Illusion 无关。仓库不包含游戏、BepInEx、
VRTK 或 DynamicBone。

## 功能范围

- 查找原版 Koikatu VR 的左右 VRTK 控制器。
- 为头发和配饰添加小型控制器 DynamicBone 碰撞球。
- 为服装使用独立且更宽的局部碰撞球，提高相邻裙摆链的命中率，同时不改变
  头发原有力度。
- 扫描原版和 Mod 服装已有的物理链，不要求 Mod 使用原版裙骨命名。
- 支持 `DynamicBone`、`DynamicBone_Ver01` 和 `DynamicBone_Ver02`。
- 把控制器 SphereCollider 追加到已有 Unity Cloth，而不覆盖服装原始碰撞器。
- 支持现代 MoreAccessories 扩展槽，以及由服装转换成的配饰物理根节点。
- 支持有界的控制器速度响应、静止接触、握持拉动和超距自动释放。
- 为头发添加头部胶囊碰撞，为裙装复用或创建大腿碰撞器。
- 换装、切换场景或关闭功能时清理插件添加的绑定并恢复临时力。
- 排除游戏原生胸部、臀部及其他身体物理，不覆盖其参数。

## 安装

从 [GitHub Releases](https://github.com/xiang-ys/KKVRHandHairCollider/releases/latest)
下载 `KKVRHandHairCollider.dll`，放置到：

```text
Koikatu/
  KoikatuVR.exe
  BepInEx/
    plugins/
      KKVRHandHairCollider/
        KKVRHandHairCollider.dll
```

启动原版 `KoikatuVR.exe`。首次加载后会生成配置文件：

```text
BepInEx/config/local.kkvr.handhaircollider.cfg
```

本插件针对原版 VRTK 架构的 Koikatu VR。尚未验证 CharaStudio、VRGIN 版本
或其他 KKVR 分支。

## 工作方式

控制器优先通过以下原版接口获取：

1. `VRTK_DeviceFinder.GetControllerLeftHand/RightHand`；
2. 回退到 `VRViveControllerManager.GetTransform(0/1)`。

插件会扫描：

- `ChaControl.objHair` 下的头发物理；
- KKAPI 配饰对象与 `ChaControl.cusAcsCmp` 对象的并集；
- `ChaControl.objClothes` 的上装和下装槽位。

服装槽中的每个已启用且具有有效根节点的 DynamicBone 都会被接入，只有明确
识别为原生身体物理的组件会被排除。禁用、无根节点或设置为 no-shake 的组件
保持不变。

头发和配饰继续使用 `0.035 m` 控制器碰撞球。服装单独使用 `0.065 m`
碰撞球，因此扩大裙装命中范围不会污染头发手感。配饰和服装还会计算相邻骨骼
之间的连续线段距离，避免只检测骨骼节点造成的空隙。

## 当前默认配置

```ini
[General]
Enabled = true
Include accessory Dynamic Bones = true
Include skirt Dynamic Bones = true
Scan interval seconds = 1
Tuning version = 7

[Controller collision]
Enabled = true
Radius meters = 0.035

[Controller force]
Enabled = true
Contact padding meters = 0.008
Strength = 0.018
Maximum force = 0.04
Minimum speed meters per second = 0.15
Velocity smoothing = 0.35

[Accessory force]
Enabled = true
Strength = 0.015
Maximum force = 0.03
Stationary contact push = 0.006
Contact padding meters = 0.012

[Clothing collision]
Radius meters = 0.065

[Clothing force]
Enabled = false
Strength = 0.012
Maximum force = 0.025
Stationary contact push = 0.006

[Grab interaction]
Enabled = true
Strength = 0.2
Maximum force = 0.04
Dead zone meters = 0.005
Maximum stretch meters = 0.22

[Head collision]
Enabled = true
Radius meters = 0.075
Height meters = 0.1
Center Y meters = 0.015

[Skirt body collision]
Enabled = true

[Unity Cloth]
Enabled = true
```

服装整链额外力默认关闭。它是为不响应局部碰撞的特殊服装保留的回退选项；
开启后可能让整条骨骼链同时移动，通常不如局部碰撞自然。

角色手部碰撞选项默认关闭，只作为没有正常控制器路径时的回退工具：

```ini
[Character hands]
Include character hand colliders = false
Create fallback hand colliders = true
```

## 物理边界

- 只有已经包含 DynamicBone 或 Unity Cloth 的头发、配饰和服装可以响应。
  静态蒙皮不会自动变成布料。
- DynamicBone 根节点是固定锚点。如果服装上半部分只绑定到固定根骨或身体骨，
  而只有裙边绑定到动态子骨，插件只能让裙边移动。
- 多条裙摆骨骼链彼此独立，没有真实布料的横向约束。更宽的局部碰撞体可以
  同时接触相邻链，但不能重建连续布面。
- 握持功能通过 DynamicBone 原有力场拉动整条链，不等同于 VRChat PhysBone
  的逐粒子抓取、姿态保持或摩擦系统。
- 实际表现仍取决于每件服装作者制作的骨骼层级、蒙皮权重、刚度和阻尼。
- 插件不会扫描或修改 `dictDynamicBoneBust`，胸部和臀部物理仍归游戏或其他
  已安装物理插件管理。

## 构建与验证

项目目标框架为 `net35`，需要本地 Koikatu 安装中的游戏与插件程序集：

```powershell
$env:KOIKATU_GAME_DIR = 'D:\Games\Koikatu'
dotnet run --project '.\tests\KKVRHandHairCollider.Tests.csproj' -c Release
dotnet build '.\src\KKVRHandHairCollider.csproj' -c Release
```

1.0 的验证结果：

- 39/39 项测试通过；
- Release 构建 0 警告、0 错误；
- 程序集版本 `1.0`，CLR `Net_2_0`；
- BepInEx 成功加载原版 `KoikatuVR.exe`；
- 测试服装的 20/20 个 Mod DynamicBone 组件全部注册；
- 原版裙装、Mod 裙装和物理配饰均经过人工互动测试。

详细证据和历史哈希见 [VALIDATION.md](VALIDATION.md)。

## 许可证

插件源代码使用 [MIT License](LICENSE)。仓库不重新分发游戏或第三方程序集。
