# Contrato de contribuição para agentes

Este arquivo é a fonte canônica para Copilot, Codex e outros agentes de IA neste repositório.

## Antes de alterar

1. Leia integralmente `AGENTS.md`, `README.md` e `ARCHITECTURE.md` antes de uma mudança relevante.
2. Inspecione o estado do repositório e preserve mudanças que não pertencem à tarefa.
3. Declare pressupostos que possam influenciar o resultado. Não invente requisitos, funcionalidades, entidades ou abstrações.

## Limites obrigatórios

- Preserve a direção Domain ← Application ← Infrastructure e a composição por Api conforme `ARCHITECTURE.md`.
- Domain não depende de infraestrutura. Mapeamentos EF Core ficam em Infrastructure.
- Controllers/endpoints, middleware e composição HTTP permanecem finos; regras pertencem ao domínio e coordenação à aplicação.
- Não crie `SharedKernel`, bases universais, repositórios genéricos, interfaces sem consumidor ou modelos especulativos.
- Validação no cliente, filtros de UI e guards nunca são autorização. Todo isolamento de tenant e toda autorização devem ser validados no servidor.
- Nunca coloque segredos, tokens ou dados clínicos reais em código, configuração versionada, fixtures, logs, exemplos ou commits.
- Não registre payloads ou identificadores sensíveis sem política aprovada.

## Forma de trabalhar

- Faça mudanças pequenas e focadas; teste os fluxos afetados.
- Use versões centralizadas em `Directory.Packages.props` e justifique dependências novas.
- Não suprima warnings, testes ou auditorias para obter pipeline verde; resolva a causa.
- Atualize documentação apenas quando comportamento, configuração ou arquitetura realmente mudar.
- Antes da entrega, execute restore, build, testes e verificação de formato.
- No relato final, declare pressupostos, riscos, arquivos relevantes e verificações executadas.

Se um requisito de segurança, tenancy ou domínio estiver ambíguo, pare antes de criar uma implementação que possa estabelecer uma premissa incorreta e solicite decisão humana.
