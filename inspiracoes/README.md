# Inspirações de interface

Referências visuais para o front end do DirectPackageInstaller. As capturas mostram um launcher de aplicativos (ArtCraft Launcher) e a tela de configurações dele.

| Arquivo | O que mostra |
|---|---|
| `launcher-grade.png` | Tela principal em grade de cartões, com filtros, ordenação e busca |
| `configuracoes.png` | Diálogo de configurações, organizado em seções |

## Traços de estilo observados

Valores aproximados, tirados a olho das capturas. Ajuste com a cor real ao implementar.

**Cor e superfícies**
- Fundo quase preto com leve tom azulado; cartões com a mesma cor e separados só por linhas finas de 1px.
- Poucas cores de destaque, usadas em etiquetas e números (azul, verde, laranja, roxo, amarelo).
- Texto principal branco e texto secundário em cinza.

**Forma**
- Cantos retos, sem arredondamento, em botões, campos, cartões e diálogos.
- Grade rígida de cartões com bordas compartilhadas.

**Tipografia**
- Títulos e nomes em sans-serif limpa, de peso médio.
- Rótulos, abas, status e cabeçalhos de seção em **monoespaçada, maiúscula, pequena e com espaçamento entre letras** (ex.: `INSTALLED · UP TO DATE`, `UPDATES`).

**Componentes**
- Botão primário: retângulo branco chapado com texto preto em maiúsculas.
- Botão secundário: contorno fino, fundo transparente, texto branco.
- Etiquetas de status: contorno fino com texto colorido em monoespaçada (verde = ok, azul = disponível).
- Número do item em um selo colorido pequeno no canto do cartão.
- Abas de filtro com contador (`ALL 13`, `INSTALLED 5`), a ativa invertida em branco.
- Seletores (ComboBox) retos, com seta à direita.

**Estrutura**
- Barra superior com marca, nome da tela, alternância Grade/Tabela, atualizar e configurações.
- Barra inferior discreta com estado da verificação e links.
- Configurações em diálogo modal, com seções marcadas por título em maiúsculas cinza: Idioma, Atualizações, Execução em segundo plano, Aparência.
- Aparência: Sistema, Escuro e Claro.

## Como isso se encaixa no plano

- **Tema:** o recurso `AppBackgroundBrush` em `App.axaml` é o ponto de partida para os tokens de cor.
- **Fase 1:** estilos comuns de botão (primário e secundário), campo, etiqueta de status e cabeçalho de seção.
- **Fase 3:** tela principal com cartão de informações do PKG e barra de status no estilo da barra inferior.
- **Fase 4:** tela de Configurações em diálogo com seções, substituindo os campos dentro do menu.
