# Core 目录声明
### 要求：
- 当前目录下必须以文件夹形式存放。
- 目录下除 `ThirdParty` 外均应为独立性强、耦合性低、与MTP业务无直接关系的功能模块。
- 凡是牵扯到MTP相关业务，请在Managers目录下实现，并提供接口。
- 如果你开发的功能模块代码量比较大，比较复杂，涉及编译文件较多，请参考下面 ThirdParty，做同样的处理。

### ThirdParty

凡是项目需要直接嵌入的第三方代码，请用文件夹包装，并完成以下内容：
1. 在对应第三方代码根目录下，添加程序集定义文件 `.asmdef`，勾选平台范围仅在 Editor 生效。 如果第三方代码仅为 C# 纯脚本引用库，请确保 `noEngineReferences` 属性为真（勾选 No Engine References）
2. 在 `Editor` 目录下的 `.asmdef` 文件请在 references 列表下添加对应的程序集名称，建议使用 Unity Editor 的 Hash Code 模式记录依赖。
