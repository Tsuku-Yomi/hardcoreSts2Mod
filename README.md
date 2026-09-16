# 硬核尖塔之观者的考验

## 简介

模仿天国拯救系列中*硬核模式*的修改，添加了几种专门为了让人恼怒而设计的新挑战机制，以折磨玩家为乐趣的模组

## 如何游玩

在选择角色中，右上角即可勾选挑战机制

## 如何开发一个新挑战机制？

1. 继承Modifier
2. 在OnRegister中添加启用该挑战机制的代码
3. 在OnUnregister中添加解除OnRegister中副作用的代码
4. 在OnInitialize中添加该挑战机制 所需要的初始化代码（但是不启动该挑战机制）
5. 填写挑战机制的名字描述巴拉巴拉
6. 完成

## 使用项目&感谢

- [**杀戮尖塔2MOD制作教程**](https://tutorials.sts2modding.com/)
- [**RitsuLib**](https://github.com/BAKAOLC/STS2-RitsuLib)
- [**RitsuLibModTemplate**](https://github.com/alkaid616/RitsuLibModTemplate)