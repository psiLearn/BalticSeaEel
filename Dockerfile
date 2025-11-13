# -------- client build stage --------
FROM node:20-alpine AS client-build
WORKDIR /src

# Install JS dependencies and build the SPA assets.
COPY package*.json ./
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
RUN dotnet restore
RUN dotnet publish src/Server/Server.fsproj -c Release -o /app/publish

# -------- runtime image --------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=server-build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "Server.dll"]
