# Example

Here is an example on how to use the TcAutomationInterface.core library for a CI/CD pipeline like Jenkins.

You can build a executable of the `Example.cs` and deploy it on your Jenkins build node and call it from the Jenkinsfile with the following command:

```bash
TwinCatBuildPipeline.exe -t Project -w C:\\Git\\TcPlc -s iTest.sln
```
