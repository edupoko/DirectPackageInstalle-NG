# Fluxo de trabalho Git

Este documento descreve como criar branches, commitar, abrir Pull Requests e publicar versões do DirectPackageInstaller.

## Visão geral

- **`DN8`** é a branch de integração e a branch padrão. Tudo que vai para o projeto passa por ela. O CI ([.github/workflows/main.yml](../.github/workflows/main.yml)) roda em push e PR para `DN8`.
- Trabalho novo sai de `DN8` em branches curtas e volta para `DN8` por Pull Request.
- Nunca commitar direto em `DN8`, exceto em casos de manutenção documentados abaixo.
- Remotos: `DPI-NG` (fork de trabalho, `edupoko/DirectPackageInstalle-NG`) e `origin` (projeto original, `marcussacana/DirectPackageInstaller`, somente leitura para atualizações).

```
DN8 ──●────────●───────●──────────────●──── (integração, sempre estável)
       \      /         \            /
        feature/x       fix/y      hotfix/z
```

## Tipos de branch

| Prefixo | Uso | Exemplo |
|---|---|---|
| `feature/` | Funcionalidade nova | `feature/tela-configuracoes` |
| `fix/` | Correção de bug | `fix/android-deteccao-ip` |
| `refactor/` | Mudança interna sem alterar comportamento | `refactor/mainviewmodel` |
| `ui/` | Mudanças visuais e de layout | `ui/tema-tokens` |
| `docs/` | Documentação | `docs/fluxo-git` |
| `chore/` | Manutenção (build, dependências, CI) | `chore/atualizar-ndk` |
| `hotfix/` | Correção urgente de versão publicada | `hotfix/crash-segmentado` |

Use nomes em minúsculas, com palavras separadas por hífen. Uma branch resolve uma coisa só.

## Passo a passo

### 1. Atualizar a base

```bash
git fetch DPI-NG
git switch DN8
git pull DPI-NG DN8
```

### 2. Criar a branch de trabalho

```bash
git switch -c feature/nome-curto DN8
```

### 3. Commitar

Use mensagens no formato **Conventional Commits**:

```
tipo(escopo opcional): descrição no imperativo

corpo opcional explicando o porquê
```

Tipos: `feat`, `fix`, `refactor`, `ui`, `docs`, `chore`, `test`.

Exemplos:
```
feat(config): adicionar tela de configurações com seções
fix(segmented): corrigir corrupção ao retomar download
ui(main): trocar botões Load/Install por ações separadas
```

Commits pequenos e focados são preferíveis a um commit gigante no fim.

### 4. Publicar e abrir o Pull Request

```bash
git push -u DPI-NG feature/nome-curto
gh pr create --base DN8 --title "feat(config): tela de configurações" --body-file -
```

O PR precisa:
- Ter como **base `DN8`**.
- Descrever o que mudou e como testar.
- Incluir capturas de tela quando houver mudança visual (desktop e, se possível, Android).
- Passar no CI (build Windows, Linux, Android; macOS tem job próprio).

### 5. Revisão e merge

- Um PR pode ser mesclado depois de revisão e CI verde.
- Preferir **Squash and merge** para manter o histórico de `DN8` limpo, ou **Rebase and merge** quando os commits já estiverem bem organizados.
- Apagar a branch após o merge.

```bash
git switch DN8
git pull DPI-NG DN8
git branch -d feature/nome-curto
git push DPI-NG --delete feature/nome-curto
```

### 6. Manter a branch atualizada durante o trabalho

Se `DN8` avançou enquanto você trabalha:

```bash
git switch feature/nome-curto
git fetch DPI-NG
git rebase DPI-NG/DN8
```

Se a branch já foi publicada e você reescreveu o histórico com rebase, atualize o remoto com:

```bash
git push --force-with-lease DPI-NG feature/nome-curto
```

`--force-with-lease` é obrigatório no lugar de `--force`. Nunca use `--force` em `DN8`.

## Hotfix (correção urgente)

1. Partir da versão publicada (tag), não do `DN8` em andamento:
   ```bash
   git switch -c hotfix/descricao <tag-da-versao>
   ```
2. Corrigir, commitar e abrir PR com base em `DN8`.
3. Depois do merge, publicar uma nova tag de patch (ex.: `v1.2.3` → `v1.2.4`).

## Versões (tags)

- Versões são marcadas com tags anotadas no formato `vMAJOR.MINOR.PATCH`.
- A tag é criada em `DN8`, depois que o estado está estável e o CI passou.

```bash
git switch DN8
git pull DPI-NG DN8
git tag -a v1.2.0 -m "Versão 1.2.0"
git push DPI-NG v1.2.0
```

## Regras

- `DN8` sempre compila e passa no CI.
- Não commitar arquivos de build, cache ou configurações pessoais (`.cache/`, `Settings.ini`, `*.user`). Se algum já estiver versionado, remover com `git rm --cached` e ajustar o `.gitignore`.
- Não reescrever histórico de `DN8`.
- Não usar `git add .` sem revisar: use `git status` e adicione arquivos específicos.
- Antes de abrir PR, rodar `git diff DN8 --stat` e confirmar que só entram as mudanças pretendidas.

## Sincronizar com o projeto original (`origin`)

O `origin` é o projeto de origem. Para trazer as atualizações dele para o fork:

```bash
git fetch origin
git switch DN8
git merge origin/DN8
git push DPI-NG DN8
```

Faça isso em uma branch `chore/sync-upstream` com PR, para revisar conflitos antes de entrar em `DN8`.

## Resumo rápido

```bash
git fetch DPI-NG && git switch DN8 && git pull DPI-NG DN8
git switch -c feature/algo DN8
# ... trabalhar e commitar ...
git push -u DPI-NG feature/algo
gh pr create --base DN8
```
