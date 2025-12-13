namespace Fake.Tools.Git
open Fake.Tools.Git
[<RequireQualifiedAccess>]
module Config =
    type UserInfo =
        { Name : string
          Email : string }
    with
        static member Create(name, email) = { Name = name; Email = email }

    let getLastAuthorUserInfo repositoryDir =
        let (name,email) = Information.getLastAuthorUserNameAndEmail repositoryDir
        UserInfo.Create(name, email)
    let setUserInfo repositoryDir overrideUserInfo =
        let defaults = getLastAuthorUserInfo repositoryDir
        let userInfo = overrideUserInfo(defaults)
        CommandHelper.directRunGitCommandAndFail repositoryDir $"config user.name \"{userInfo.Name}\""
        CommandHelper.directRunGitCommandAndFail repositoryDir $"config user.email \"{userInfo.Email}\""
