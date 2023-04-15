namespace Fake.Tools.Git
module Information =
    let getLastAuthorUserNameAndEmail repositoryDir =
        let name = CommandHelper.runSimpleGitCommand repositoryDir "log -n 1 --pretty=format:%an"
        let email = CommandHelper.runSimpleGitCommand repositoryDir "log -n 1 --pretty=format:%ae"
        (name, email)

