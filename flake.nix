{
  description = "Voice2Action dev environment";

  inputs = {
    # Use a recent nixpkgs; allow user to update later.
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable"; # Using unstable to ensure .NET SDK 9 availability (FR008).
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = {
    self,
    nixpkgs,
    flake-utils,
  }:
    flake-utils.lib.eachDefaultSystem (system: let
      pkgs = import nixpkgs {inherit system;};
    in {
      formatter = pkgs.alejandra;

      # Full-featured interactive dev shell (pulls secrets via 1Password CLI)
      devShells.default = pkgs.mkShell {
        name = "voice2action-dev";
        buildInputs = with pkgs; [
          dotnet-sdk_9
          bashInteractive
          bash-completion
          jq
        ];
        DOTNET_CLI_TELEMETRY_OPTOUT = 1;
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 1;
        DOTNET_NOLOGO = 1;
        DOTNET_MULTILEVEL_LOOKUP = 0;
        shellHook = ''
          export DOTNET_ROOT=${pkgs.dotnet-sdk_9}/share/dotnet
          # Populate secrets for interactive development (omit in CI / build shell)
          export AZURE_OPENAI_API_KEY=$(op item get "Azure OpenAI API Key" --vault Private --fields label=key --format json | jq -r '.value')
          export AZURE_OPENAI_ENDPOINT=$(op item get "Azure OpenAI API Key" --vault Private --fields label=url --format json | jq -r '.value')

          # --- Basic bash history + completion support (ignored if not using bash) ---
          if [ -n "$BASH_VERSION" ]; then
            export HISTFILE=''${PWD}/.nix-shell-history
            export HISTSIZE=5000
            export HISTCONTROL=ignoredups:erasedups
            shopt -s histappend 2>/dev/null || true
            # Ensure file exists to avoid warnings
            touch "$HISTFILE"
            # Append new commands and re-read so history is shared across concurrent shells
            if [ -n "''${PROMPT_COMMAND}" ]; then
              PROMPT_COMMAND="history -a; history -n; ''${PROMPT_COMMAND}"
            else
              PROMPT_COMMAND="history -a; history -n"
            fi
            # Load bash completion if available
            if [ -f ${pkgs.bash-completion}/etc/profile.d/bash_completion.sh ]; then
              source ${pkgs.bash-completion}/etc/profile.d/bash_completion.sh
            fi
          fi
        '';
      };

      # Minimal build shell for CI / non-interactive builds.
      # Usage: nix develop .#build -c dotnet build
      # Does NOT attempt to fetch secrets (runtime-only). Keeps environment pure and deterministic.
      devShells.build = pkgs.mkShell {
        name = "voice2action-build";
        buildInputs = with pkgs; [
          dotnet-sdk_9
        ];
        DOTNET_CLI_TELEMETRY_OPTOUT = 1;
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 1;
        DOTNET_NOLOGO = 1;
        DOTNET_MULTILEVEL_LOOKUP = 0;
        # Provide DOTNET_ROOT only; secrets intentionally omitted.
        shellHook = ''
          export DOTNET_ROOT=${pkgs.dotnet-sdk_9}/share/dotnet
          echo "[build shell] Secrets not loaded (expected)." >&2
        '';
      };
    });
}
