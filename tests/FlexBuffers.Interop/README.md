# FlexBuffers 独立互操作检查

在仓库根运行 C# 输出器，再用官方 Python FlatBuffers 25.12.19 读取输出。普通单元测试不依赖 Python；它们使用已保存的官方字节夹具。

```powershell
dotnet run --project tests/FlexBuffers.Interop -c Release -- artifacts/flex-interop
python tests/FlexBuffers.Interop/verify.py artifacts/flex-interop
```

C# 的可选第二参数是原版 BTL 样本目录。每份样本会强制重新编码树库；非空样本还会修改名称并加入混合类型未知字段后编码。另检查原载荷经过工作 JSON、克隆和外层导出仍逐字节一致。Python 会独立读取原版 payload 作为预期，不采用 C# 解码结果作为样本真值。全部输入只读，输出集中到第一参数目录。

大载荷用例覆盖超出两字节的字符串长度与偏移、64 位边界、null、布尔、浮点与负零；官方读取器逐键查找，能发现未排序 Map。

需要 `flatbuffers==25.12.19` Python 包，也可通过 `PYTHONPATH` 使用本地已有的官方包，不要求安装到系统。原版样本是本地资料，不随源码分发。夹具来源和重新生成方法见 `tests/FrontEdit.Tests/Fixtures/generate_flexbuffers.py`。
