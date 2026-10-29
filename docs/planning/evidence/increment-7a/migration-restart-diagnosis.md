# Reexecução da migration no Compose

Em 29/10/2026, a primeira subida terminou com migrate exit 0/API healthy. A reconstrução posterior falhou com PostgresException ao recriar operation.operations. A consulta somente leitura executada pelo usuário retornou:

```text
schemaname | tablename
public     | __EFMigrationsHistory
operation  | __EFMigrationsHistory
```

Usuário local operation coincide com o schema criado, mudando current_schema() após a primeira migration. A fábrica agora configura MigrationsHistoryTable("__EFMigrationsHistory", "public"). O teste da ferramenta aplica, verifica e reaplica com SearchPath=operation,public. Não foram apagados dados/volume nem alterado manualmente o histórico. O histórico adicional em operation fica sem uso.

A ferramenta agora retorna SQLSTATE sanitizado para PostgresException. O aviso libgssapi_krb5.so.2 é descrito como fallback na [documentação Npgsql 10](https://www.npgsql.org/doc/release-notes/10.0.html); o Compose de senha local explicita GSS Encryption Mode=Disable. Isso não é uma alteração da política TLS de produção.
