# Arquitetura do Brothers Care

## Visão geral

A solução aplica Clean Architecture para manter regras de negócio independentes de HTTP, banco e fornecedores. DDD orientará linguagem, limites e invariantes quando requisitos reais forem aprovados; esta fundação não antecipa agregados, value objects ou abstrações genéricas.

```text
Api ───────────────> Application ───────────────> Domain
 │                         ▲
 └──> Infrastructure ──────┘
              └───────────────────────────────> Domain
```

## Responsabilidades

- **Domain**: modelo, invariantes e comportamento do domínio. Não referencia outras camadas nem frameworks de infraestrutura.
- **Application**: coordena casos de uso e define contratos necessários por eles. Depende somente de Domain entre os projetos da solução.
- **Infrastructure**: implementa contratos para PostgreSQL, mensageria e serviços externos. Pode depender de Application e Domain. Mapeamentos EF Core pertencem aqui; o domínio não recebe atributos de persistência.
- **Api**: composition root, configuração HTTP e tradução de entrada/saída. Referencia Application e Infrastructure para composição. Endpoints, controllers e middleware permanecem finos e não contêm regras de domínio.

`Program.cs` é o ponto de composição. Cada camada expõe apenas seu registro de serviços. Novas features devem ser organizadas por capacidade de negócio dentro das camadas existentes; novos projetos ou kernels compartilhados exigem uma necessidade concreta.

## Decisões desta base

- .NET 10 LTS, nullable e analisadores recomendados habilitados; warnings são erros.
- versões NuGet centralizadas e auditoria de vulnerabilidades habilitada;
- Problem Details como formato de erro HTTP e exception handler central;
- logging estruturado pelas abstrações nativas de `ILogger` e configuração por categoria;
- OpenAPI e Swagger UI somente em `Development`;
- CORS desabilitado por padrão e restrito a origens configuradas;
- liveness não depende de serviços externos; readiness aceitará checks com tag `ready`;
- testes de arquitetura sem uma biblioteca adicional e testes HTTP com o host de teste oficial do ASP.NET Core.

## Persistência

PostgreSQL e EF Core são a direção planejada, mas dependem de um modelo aprovado. O `DbContext`, provider, entidades, configurações, schema e migrações serão introduzidos junto da primeira necessidade real. A configuração será opcional por ambiente durante o desenvolvimento e credenciais virão de variáveis, user secrets ou um cofre; nunca do repositório.

Entidades de domínio não serão entidades anêmicas moldadas pelo EF. Configurações `IEntityTypeConfiguration` e demais detalhes de armazenamento permanecerão em Infrastructure. Migrações deverão ser revisáveis e executadas por processo operacional explícito.

## Multi-tenancy, identidade e autorização

O produto será multi-tenant. O isolamento do tenant deve ser aplicado e validado no servidor em **cada leitura e escrita**. Um tenant nunca pode ser confiado somente por parâmetro, header ou corpo enviado pelo cliente. O contexto confiável deverá resultar da identidade autenticada e de membership validado no servidor.

Autenticação, memberships, papéis e escopo por unidade serão modelados quando identity e tenants forem implementados. Autorização será server-side, próxima do caso de uso e testada; filtros de UI e guards do frontend não são controles de segurança. A suíte futura deve incluir tentativas de acesso cruzado entre empresas, inclusive por identificadores conhecidos, consultas, alterações e operações em lote.

Não há nesta etapa tenant resolution, login, claims, membership ou autorização. Nenhum mecanismo fictício deve ser confundido com proteção real.

## Segurança e privacidade

Antes de armazenar dados reais de saúde, devem ser definidos e validados: modelo de ameaças, base legal e retenção, classificação de dados, criptografia e gestão de chaves, segregação e menor privilégio, trilha de auditoria imutável, resposta a incidentes, backup/restauração testados, exclusão/exportação, mascaramento de ambientes não produtivos, política de logs e revisão de fornecedores.

Segredos e dados clínicos reais são proibidos em código, fixtures, logs e commits. Problem Details não deve expor exceções internas em produção. Rate limiting, limites de payload, headers, HTTPS na borda, autenticação e autorização deverão ser especificados antes da exposição pública.

## Observabilidade e integrações

O logging atual usa propriedades estruturadas via `ILogger`. Correlação, métricas, tracing distribuído, OpenTelemetry, alertas e SLOs serão adicionados quando houver topologia e operação definidas, evitando vendor lock-in prematuro. Dados clínicos e tokens nunca devem aparecer em telemetria.

Integrações externas serão adaptadores em Infrastructure, atrás de contratos motivados por casos de uso. Devem possuir timeout, cancelamento, resiliência e idempotência conforme o risco real; não há clientes fictícios nesta base.

## Não objetivos

Esta base não implementa funcionalidades, modelo de domínio, persistência, containers, autenticação ou implantação. Também não cria `SharedKernel`, repositórios genéricos, entidades base, mediadores ou interfaces sem consumidor. Mudanças arquiteturais reais devem atualizar este documento e registrar seus trade-offs.
