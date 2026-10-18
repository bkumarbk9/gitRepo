# Copyright (C) 2026 Bharath K A (bkumarbk9)
# SPDX-License-Identifier: AGPL-3.0-only
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU Affero General Public License version 3
# as published by the Free Software Foundation.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU Affero General Public License for more details.
#
# You should have received a copy of the GNU Affero General Public License
# along with this program.  If not, see <https://www.gnu.org/licenses/>.

# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage builds the Angular client; its static files are served by the API from wwwroot
FROM node:24-alpine AS client
WORKDIR /client
COPY ["AzDocIntelligenceAI.Client/document-extractor/package.json", "AzDocIntelligenceAI.Client/document-extractor/package-lock.json", "./"]
RUN npm ci
COPY AzDocIntelligenceAI.Client/document-extractor/ .
RUN npx ng build --configuration production

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["AzDocIntelligenceAI/AzDocIntelligenceAI.csproj", "AzDocIntelligenceAI/"]
RUN dotnet restore "./AzDocIntelligenceAI/AzDocIntelligenceAI.csproj"
COPY . .
WORKDIR "/src/AzDocIntelligenceAI"
RUN dotnet build "./AzDocIntelligenceAI.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./AzDocIntelligenceAI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:BuildClient=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=client /client/dist/document-extractor/browser ./wwwroot
ENTRYPOINT ["dotnet", "AzDocIntelligenceAI.dll"]