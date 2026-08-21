# Managers 目录声明

当前目录为 Core 模块功能以及 Data 提供服务接口。

### 要求：
  - 当前目录下文件命名格式只有两种：
    - “{xxx}Manager.cs” 
    - "Services/I{xxx}Service.cs"+“Services/Impls/{xxx}Service.cs” 
  - 实现方式：
    1. 单例模式
        
        当你的服务实现仅包括运行时数据转换，或服务单一独立，推荐使用此方式
       ```c#
       public sealed class SingletonManager
       {
           private static SingletonManager s_instance;
           public static SingletonManager Instance => s_instance ??= new SingletonManager();
           SingletonManager(){}
       } 
       ```
    2. interface - implement 模式 
       
       当你的服务实现动用网络 / 业务逻辑拆分后有明显先后顺序时，请使用此方式
       ```c#
       // 接口类 IBusinessService.cs
       public interface IBusinessService
       {
          void FuncTouchNetwork();
          void FuncTouchDisk();
       }
       // 实现类 BusinessService.cs
       public class BusinessService : IBusinessService
       {
          public void FuncTouchNetwork()
          {
             // Implement...
          }
          // other funcs...
       }
        ```
        写好上述类之后，进入 `ServiceManager.cs` 在 `Initialize` 方法下填写服务注册信息（此时有先后依赖的服务按顺序注册）：
        ```c#
        public static Initialize()
        {
            // ...
            ServiceLocator.Register<IBusinessService>(new BusinessService);
        }
        ```
        使用服务时，在需要的类里添加对应服务接口的私有成员变量，采用懒加载模式赋值：
        ```c#
        public class XXXClass
        {
            private IBusinessService BusinessService => ServiceLocator.Get<IBusinessService>();
            
            void Func()
            {
                BusinessService.XXXFunc();
            }
        }
        ```
        或使用方法内局部变量形式：
        ```c#
        void Func()
        {
            var businessService = ServiceLocator.Get<IBusinessService>();
            businessService.XXXFunc();
        }
        ```