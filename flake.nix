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
      devShells.default = pkgs.mkShell {
        name = "voice2action-dev";
        buildInputs = with pkgs; [
          dotnet-sdk_9
          jq
        ];
        DOTNET_CLI_TELEMETRY_OPTOUT = 1;
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 1;
        DOTNET_NOLOGO = 1;
        DOTNET_MULTILEVEL_LOOKUP = 0;
        shellHook = ''
          export DOTNET_ROOT=${pkgs.dotnet-sdk_9}/share/dotnet
          export AZURE_OPENAI_API_KEY=$(op item get "Azure OpenAI API Key" --vault Private --fields label=key --format json | jq -r '.value')
          export AZURE_OPENAI_ENDPOINT=$(op item get "Azure OpenAI API Key" --vault Private --fields label=url --format json | jq -r '.value')
        '';
      };
    });
}
