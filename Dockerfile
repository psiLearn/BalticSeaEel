# -------- client build stage --------
FROM node:20-bookworm AS client-build
WORKDIR /src

# Install .NET SDK (needed by vite-plugin-fable during npm install).
RUN apt-get update && \
    apt-get install -y wget ca-certificates && \
    wget https://dot.net/v1/dotnet-install.sh && \
    chmod +x dotnet-install.sh && \
    ./dotnet-install.sh --version 8.0.301 && \
    rm dotnet-install.sh && \
    rm -rf /var/lib/apt/lists/*
ENV DOTNET_ROOT=/root/.dotnet
ENV PATH="${PATH}:/root/.dotnet:/root/.dotnet/tools"

# Install JS dependencies and build the SPA assets.
COPY package*.json ./
COPY scripts ./scripts
RUN npm install
COPY . .
RUN npm run build

# -------- server publish stage --------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS server-build
WORKDIR /src

# Copy the full repository plus the generated client assets.
COPY . .
COPY --from=client-build /src/src/Server/wwwroot ./src/Server/wwwroot

# Restore and publish the Giraffe server (includes the client assets).
RUN dotnet restore src/Server/Server.fsproj
RUN dotnet publish src/Server/Server.fsproj -c Release -o /app/publish

# -------- runtime image --------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=server-build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "Server.dll"]
