# MultilayerCache

A multilayer caching solution in C# with in-memory and Redis layers, supporting Protobuf serialization.

---

## Table of Contents

1. [Project Overview](#project-overview)  
2. [Prerequisites](#prerequisites)  
3. [Folder Structure](#folder-structure)  
4. [Setup](#setup)  
5. [Build & Run](#build--run)  
6. [Docker](#docker)  
7. [Testing](#testing)  
8. [Usage Example](#usage-example)  
9. [Notes](#notes)

---

## Project Overview

This project demonstrates a **multilayer caching system** in C#:

- **In-memory cache** for fast access.  
- **Redis cache** as a distributed layer.  
- **Protobuf serialization** for efficient storage and transfer.  
- **Demo & test projects** for experimentation and validation.  

It’s designed to be a foundation for **high-performance caching strategies** in distributed applications.

---

## Prerequisites

Before building and running the project:

1. **.NET SDK 8.0** or higher  
   - [Download .NET SDK](https://dotnet.microsoft.com/download)  
2. **protoc (Protocol Buffers compiler)**  
   - [Download latest release](https://github.com/protocolbuffers/protobuf/releases/latest)  
   - Extract to a folder and add `bin` to your PATH.  
   - Verify: `protoc --version`  
3. **Docker Desktop** (for running Redis)  
   - Ensure it is installed and running.  
   - Verify: `docker --version`  

> ⚠️ On Windows, make sure your user has permission to run Docker commands.

---

## Folder Structure

