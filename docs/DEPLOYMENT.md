# Deployment Guide

This guide covers various deployment strategies for FastTTSR in production environments.

## Table of Contents

- [Docker Deployment](#docker-deployment)
- [Cloud Platforms](#cloud-platforms)
- [Kubernetes](#kubernetes)
- [Production Checklist](#production-checklist)
- [Monitoring](#monitoring)
- [Backup and Recovery](#backup-and-recovery)

---

## Docker Deployment

### Single Container Deployment

The simplest production deployment using Docker Compose.

#### 1. Prepare the Environment

```bash
# Create required directories
mkdir -p model-cache assets

# Set appropriate permissions
chmod 755 model-cache assets
```

#### 2. Create Production docker-compose.yml

```yaml
version: '3.8'

services:
  fastttsr:
    image: fhrozen/fast-ttsr:latest
    restart: unless-stopped
    ports:
      - "127.0.0.1:5768:5768"  # Only bind to localhost
    environment:
      HTTP_PORT: 5768
      MODEL_CACHE_DIR: /cache
      ESPEAK_DATA_DIR: /app/assets/espeak-ng-data
      MODEL_IDLE_TIMEOUT_SECONDS: 60
      Logging__LogLevel__Default: Warning
      Logging__LogLevel__Microsoft.AspNetCore: Error
    volumes:
      - ./model-cache:/cache
      - ./assets:/app/assets:ro
    deploy:
      resources:
        limits:
          memory: 4G
          cpus: '2.0'
        reservations:
          memory: 1G
          cpus: '0.5'
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:5768/health"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 120s
    networks:
      - fastttsr-network

  # Optional: nginx reverse proxy
  nginx:
    image: nginx:alpine
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf:ro
      - ./ssl:/etc/nginx/ssl:ro
    depends_on:
      - fastttsr
    networks:
      - fastttsr-network

networks:
  fastttsr-network:
    driver: bridge
```

#### 3. Deploy

```bash
docker compose up -d
```

#### 4. Verify Deployment

```bash
# Check container status
docker compose ps

# View logs
docker compose logs -f

# Test API
curl http://localhost:5768/health
```

---

## Cloud Platforms

### AWS Deployment

#### Option 1: AWS ECS (Elastic Container Service)

**1. Create ECR Repository**

```bash
aws ecr create-repository --repository-name fastttsr
```

**2. Build and Push Image**

```bash
# Login to ECR
aws ecr get-login-password --region us-east-1 | \
  docker login --username AWS --password-stdin \
  123456789012.dkr.ecr.us-east-1.amazonaws.com

# Build image
docker build -t fastttsr .

# Tag and push
docker tag fastttsr:latest \
  123456789012.dkr.ecr.us-east-1.amazonaws.com/fastttsr:latest
docker push 123456789012.dkr.ecr.us-east-1.amazonaws.com/fastttsr:latest
```

**3. Create EFS for Model Cache**

```bash
aws efs create-file-system \
  --performance-mode generalPurpose \
  --throughput-mode bursting \
  --tags Key=Name,Value=fastttsr-models
```

**4. Create ECS Task Definition**

```json
{
  "family": "fastttsr",
  "networkMode": "awsvpc",
  "requiresCompatibilities": ["FARGATE"],
  "cpu": "2048",
  "memory": "4096",
  "containerDefinitions": [
    {
      "name": "fastttsr",
      "image": "123456789012.dkr.ecr.us-east-1.amazonaws.com/fastttsr:latest",
      "essential": true,
      "environment": [
        {"name": "HTTP_PORT", "value": "5768"},
        {"name": "MODEL_IDLE_TIMEOUT_SECONDS", "value": "60"}
      ],
      "portMappings": [
        {
          "containerPort": 5768,
          "protocol": "tcp"
        }
      ],
      "mountPoints": [
        {
          "sourceVolume": "model-cache",
          "containerPath": "/cache"
        }
      ],
      "logConfiguration": {
        "logDriver": "awslogs",
        "options": {
          "awslogs-group": "/ecs/fastttsr",
          "awslogs-region": "us-east-1",
          "awslogs-stream-prefix": "ecs"
        }
      },
      "healthCheck": {
        "command": ["CMD-SHELL", "curl -f http://localhost:5768/health || exit 1"],
        "interval": 30,
        "timeout": 5,
        "retries": 3,
        "startPeriod": 120
      }
    }
  ],
  "volumes": [
    {
      "name": "model-cache",
      "efsVolumeConfiguration": {
        "fileSystemId": "fs-12345678",
        "transitEncryption": "ENABLED"
      }
    }
  ]
}
```

**5. Create ECS Service**

```bash
aws ecs create-service \
  --cluster fastttsr-cluster \
  --service-name fastttsr \
  --task-definition fastttsr:1 \
  --desired-count 2 \
  --launch-type FARGATE \
  --network-configuration "awsvpcConfiguration={subnets=[subnet-12345,subnet-67890],securityGroups=[sg-12345],assignPublicIp=ENABLED}" \
  --load-balancers "targetGroupArn=arn:aws:elasticloadbalancing:...,containerName=fastttsr,containerPort=5768"
```

#### Option 2: AWS App Runner

**1. Create apprunner.yaml**

```yaml
version: 1.0
runtime: python3.11
build:
  commands:
    build:
      - docker build -t fastttsr .
run:
  runtime-version: 3.11
  command: dotnet FastTTSR.Api.dll
  network:
    port: 5768
  env:
    - name: MODEL_CACHE_DIR
      value: /cache
    - name: MODEL_IDLE_TIMEOUT_SECONDS
      value: 60
```

**2. Deploy**

```bash
aws apprunner create-service \
  --service-name fastttsr \
  --source-configuration "ImageRepository={ImageIdentifier=123456789012.dkr.ecr.us-east-1.amazonaws.com/fastttsr:latest,ImageRepositoryType=ECR}"
```

---

### Azure Deployment

#### Option 1: Azure Container Instances (ACI)

**1. Create Resource Group**

```bash
az group create --name fastttsr-rg --location eastus
```

**2. Create Azure File Share (for model cache)**

```bash
az storage account create \
  --name faststtrstorage \
  --resource-group fastttsr-rg \
  --sku Standard_LRS

az storage share create \
  --name model-cache \
  --account-name faststtrstorage
```

**3. Deploy Container**

```bash
az container create \
  --resource-group fastttsr-rg \
  --name fastttsr \
  --image fhrozen/fast-ttsr:latest \
  --cpu 2 \
  --memory 4 \
  --ports 5768 \
  --dns-name-label fastttsr \
  --environment-variables \
    HTTP_PORT=5768 \
    MODEL_IDLE_TIMEOUT_SECONDS=60 \
  --azure-file-volume-account-name faststtrstorage \
  --azure-file-volume-account-key $STORAGE_KEY \
  --azure-file-volume-share-name model-cache \
  --azure-file-volume-mount-path /cache
```

#### Option 2: Azure Container Apps

**1. Create Container App Environment**

```bash
az containerapp env create \
  --name fastttsr-env \
  --resource-group fastttsr-rg \
  --location eastus
```

**2. Create Container App**

```bash
az containerapp create \
  --name fastttsr \
  --resource-group fastttsr-rg \
  --environment fastttsr-env \
  --image fhrozen/fast-ttsr:latest \
  --cpu 2.0 \
  --memory 4Gi \
  --min-replicas 1 \
  --max-replicas 5 \
  --target-port 5768 \
  --ingress external \
  --env-vars \
    HTTP_PORT=5768 \
    MODEL_IDLE_TIMEOUT_SECONDS=60
```

---

### Google Cloud Platform

#### Cloud Run Deployment

**1. Enable Required APIs**

```bash
gcloud services enable \
  run.googleapis.com \
  containerregistry.googleapis.com
```

**2. Build and Push to GCR**

```bash
# Configure Docker
gcloud auth configure-docker

# Build and push
docker build -t gcr.io/PROJECT_ID/fastttsr .
docker push gcr.io/PROJECT_ID/fastttsr
```

**3. Deploy to Cloud Run**

```bash
gcloud run deploy fastttsr \
  --image gcr.io/PROJECT_ID/fastttsr \
  --platform managed \
  --region us-central1 \
  --memory 4Gi \
  --cpu 2 \
  --max-instances 10 \
  --set-env-vars="HTTP_PORT=8080,MODEL_IDLE_TIMEOUT_SECONDS=60" \
  --allow-unauthenticated
```

**4. Create Persistent Disk (Optional)**

```bash
# Cloud Run doesn't support persistent disks directly
# Use Cloud Storage or Filestore for model cache
```

---

## Kubernetes

### Helm Chart Deployment

#### 1. Create values.yaml

```yaml
replicaCount: 3

image:
  repository: fhrozen/fast-ttsr
  tag: latest
  pullPolicy: IfNotPresent

service:
  type: LoadBalancer
  port: 80
  targetPort: 5768

ingress:
  enabled: true
  className: nginx
  annotations:
    cert-manager.io/cluster-issuer: letsencrypt-prod
  hosts:
    - host: tts.example.com
      paths:
        - path: /
          pathType: Prefix
  tls:
    - secretName: fastttsr-tls
      hosts:
        - tts.example.com

resources:
  requests:
    memory: "1Gi"
    cpu: "500m"
  limits:
    memory: "4Gi"
    cpu: "2000m"

env:
  - name: HTTP_PORT
    value: "5768"
  - name: MODEL_IDLE_TIMEOUT_SECONDS
    value: "60"
  - name: MODEL_CACHE_DIR
    value: "/cache"

persistence:
  enabled: true
  storageClass: "standard"
  accessMode: ReadWriteMany
  size: 20Gi
  mountPath: /cache

autoscaling:
  enabled: true
  minReplicas: 2
  maxReplicas: 10
  targetCPUUtilizationPercentage: 70

healthCheck:
  livenessProbe:
    httpGet:
      path: /health
      port: 5768
    initialDelaySeconds: 120
    periodSeconds: 30
  readinessProbe:
    httpGet:
      path: /health
      port: 5768
    initialDelaySeconds: 60
    periodSeconds: 10
```

#### 2. Create Kubernetes Manifests

**deployment.yaml:**
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: fastttsr
  labels:
    app: fastttsr
spec:
  replicas: 3
  selector:
    matchLabels:
      app: fastttsr
  template:
    metadata:
      labels:
        app: fastttsr
    spec:
      containers:
      - name: fastttsr
        image: fhrozen/fast-ttsr:latest
        ports:
        - containerPort: 5768
        env:
        - name: HTTP_PORT
          value: "5768"
        - name: MODEL_IDLE_TIMEOUT_SECONDS
          value: "60"
        - name: MODEL_CACHE_DIR
          value: "/cache"
        resources:
          requests:
            memory: "1Gi"
            cpu: "500m"
          limits:
            memory: "4Gi"
            cpu: "2000m"
        volumeMounts:
        - name: model-cache
          mountPath: /cache
        livenessProbe:
          httpGet:
            path: /health
            port: 5768
          initialDelaySeconds: 120
          periodSeconds: 30
        readinessProbe:
          httpGet:
            path: /health
            port: 5768
          initialDelaySeconds: 60
          periodSeconds: 10
      volumes:
      - name: model-cache
        persistentVolumeClaim:
          claimName: fastttsr-cache
```

**service.yaml:**
```yaml
apiVersion: v1
kind: Service
metadata:
  name: fastttsr
spec:
  type: LoadBalancer
  ports:
  - port: 80
    targetPort: 5768
    protocol: TCP
  selector:
    app: fastttsr
```

**pvc.yaml:**
```yaml
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: fastttsr-cache
spec:
  accessModes:
    - ReadWriteMany
  storageClassName: nfs-client  # Use NFS or similar for shared storage
  resources:
    requests:
      storage: 20Gi
```

**hpa.yaml:**
```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: fastttsr-hpa
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: fastttsr
  minReplicas: 2
  maxReplicas: 10
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
```

#### 3. Deploy

```bash
kubectl apply -f deployment.yaml
kubectl apply -f service.yaml
kubectl apply -f pvc.yaml
kubectl apply -f hpa.yaml
```

---

## Production Checklist

### Pre-Deployment

- [ ] **Security Audit**
  - [ ] Use HTTPS/TLS
  - [ ] Implement rate limiting
  - [ ] Add authentication (API keys)
  - [ ] Configure CORS properly
  - [ ] Review exposed ports

- [ ] **Performance Testing**
  - [ ] Load testing completed
  - [ ] Stress testing completed
  - [ ] Latency benchmarks acceptable
  - [ ] Resource usage profiled

- [ ] **Resource Planning**
  - [ ] CPU/memory requirements calculated
  - [ ] Storage requirements estimated
  - [ ] Network bandwidth planned
  - [ ] Cost estimates reviewed

- [ ] **Monitoring Setup**
  - [ ] Logging configured
  - [ ] Metrics collection enabled
  - [ ] Alerting rules defined
  - [ ] Health checks configured

### Post-Deployment

- [ ] **Verification**
  - [ ] Health check responding
  - [ ] API endpoints functional
  - [ ] Models downloading correctly
  - [ ] Audio generation working

- [ ] **Monitoring**
  - [ ] Logs streaming correctly
  - [ ] Metrics being collected
  - [ ] Alerts configured and tested
  - [ ] Dashboard created

- [ ] **Documentation**
  - [ ] Deployment documented
  - [ ] Runbook created
  - [ ] Contact information updated
  - [ ] Rollback procedures documented

---

## Monitoring

### Health Checks

**Endpoint:**
```
GET /health
```

**Response:**
```json
{
  "status": "ok"
}
```

**Load Balancer Configuration:**
- **Path:** `/health`
- **Interval:** 30 seconds
- **Timeout:** 5 seconds
- **Healthy threshold:** 2
- **Unhealthy threshold:** 3

### Logging

**Structured Logging Example:**
```json
{
  "timestamp": "2026-05-12T10:30:00Z",
  "level": "Information",
  "message": "Speech synthesis completed",
  "model": "kokoro-q4",
  "duration_ms": 156,
  "input_length": 42,
  "voice": "af_bella"
}
```

**Centralized Logging:**
- **AWS:** CloudWatch Logs
- **Azure:** Azure Monitor
- **GCP:** Cloud Logging
- **Self-hosted:** ELK Stack, Loki

### Metrics to Monitor

| Metric | Type | Alert Threshold |
|--------|------|-----------------|
| Request Rate | Counter | - |
| Response Time | Histogram | p95 > 500ms |
| Error Rate | Counter | > 5% |
| CPU Usage | Gauge | > 80% |
| Memory Usage | Gauge | > 85% |
| Model Load Time | Histogram | p95 > 1s |
| Cache Hit Rate | Gauge | < 80% |
| Active Connections | Gauge | - |

### Prometheus Metrics (Future)

```
# HELP fastttsr_requests_total Total number of TTS requests
# TYPE fastttsr_requests_total counter
fastttsr_requests_total{model="kokoro-q4",status="success"} 1234

# HELP fastttsr_synthesis_duration_seconds Time spent synthesizing speech
# TYPE fastttsr_synthesis_duration_seconds histogram
fastttsr_synthesis_duration_seconds_bucket{model="kokoro-q4",le="0.1"} 245
fastttsr_synthesis_duration_seconds_bucket{model="kokoro-q4",le="0.5"} 892
```

### Grafana Dashboard

Key panels:
1. Request rate over time
2. Response time percentiles (p50, p95, p99)
3. Error rate
4. CPU and memory usage
5. Active models
6. Cache statistics

---

## Backup and Recovery

### Backup Strategy

**What to Backup:**
1. **Model Cache** - Optional (can re-download)
2. **Configuration Files** - Critical
3. **Custom Assets** - Critical if customized
4. **Logs** - For audit trail

**Backup Schedule:**
- Configuration: On every change
- Logs: Daily rotation
- Model cache: Not necessary (re-downloadable)

### Disaster Recovery

**Recovery Time Objective (RTO):** < 1 hour
**Recovery Point Objective (RPO):** < 1 hour

**Recovery Steps:**
1. Deploy new instance from backed-up configuration
2. Restore configuration files
3. Models will auto-download on first request
4. Verify with health check and test synthesis

### High Availability Setup

**Multi-Region Deployment:**
```
Region 1 (Primary)          Region 2 (Backup)
├── Load Balancer          ├── Load Balancer
├── Instance 1             ├── Instance 1
├── Instance 2             ├── Instance 2
└── Shared Model Cache     └── Shared Model Cache
```

**Failover Strategy:**
- DNS-based failover (Route53, Cloud DNS)
- Health check monitoring
- Automatic failover < 5 minutes
- Geo-routing for performance

---

## Scaling Strategies

### Vertical Scaling

Increase resources per instance:
- **Small:** 1 CPU, 2GB RAM (< 10 req/sec)
- **Medium:** 2 CPU, 4GB RAM (< 50 req/sec)
- **Large:** 4 CPU, 8GB RAM (< 100 req/sec)

### Horizontal Scaling

Add more instances:
- **Stateless design** - Easy to scale
- **Shared model cache** - NFS, EFS, Azure Files
- **Load balancing** - Round robin or least connections
- **Auto-scaling** - Based on CPU/memory/queue length

### Auto-Scaling Rules

**Scale Out:**
- CPU > 70% for 5 minutes
- Memory > 80% for 5 minutes
- Request queue length > 10

**Scale In:**
- CPU < 30% for 10 minutes
- Memory < 50% for 10 minutes
- Request queue length < 2

---

## Cost Optimization

### Tips

1. **Use Quantized Models** - kokoro-q4 uses less memory
2. **Enable Idle Unloading** - Free memory when not in use
3. **Right-Size Instances** - Don't over-provision
4. **Use Spot/Preemptible Instances** - 70% cost savings
5. **Cache Aggressively** - Reduce recomputation
6. **Regional Deployment** - Avoid cross-region data transfer

### Cost Estimates

**AWS ECS (us-east-1):**
- Fargate (2 vCPU, 4GB): ~$40/month
- EFS (20GB): ~$6/month
- ALB: ~$22/month
- **Total:** ~$68/month per instance

**Azure Container Instances:**
- 2 vCPU, 4GB: ~$50/month
- Azure Files (20GB): ~$10/month
- **Total:** ~$60/month per instance

**GCP Cloud Run:**
- Pay per request (1M requests): ~$20/month
- Storage (20GB): ~$5/month
- **Total:** ~$25/month for low traffic

---

## Security Best Practices

1. **Use Secrets Management**
   - AWS Secrets Manager
   - Azure Key Vault
   - Kubernetes Secrets

2. **Enable TLS/HTTPS**
   - Let's Encrypt for certificates
   - TLS 1.3 minimum

3. **Implement Rate Limiting**
   - API Gateway
   - nginx limit_req
   - Middleware

4. **Add Authentication**
   - API keys
   - JWT tokens
   - OAuth 2.0

5. **Network Security**
   - VPC/VNET isolation
   - Security groups/NSGs
   - Web Application Firewall

6. **Regular Updates**
   - Update base images
   - Patch vulnerabilities
   - Update dependencies

---

## Rollback Procedures

### Docker

```bash
# Tag current version
docker tag fastttsr:latest fastttsr:backup

# Rollback to previous version
docker pull fhrozen/fast-ttsr:previous-tag
docker compose up -d
```

### Kubernetes

```bash
# Rollback deployment
kubectl rollout undo deployment/fastttsr

# Rollback to specific revision
kubectl rollout undo deployment/fastttsr --to-revision=2

# Check rollout status
kubectl rollout status deployment/fastttsr
```

### Cloud Services

**AWS ECS:**
```bash
aws ecs update-service \
  --cluster fastttsr-cluster \
  --service fastttsr \
  --task-definition fastttsr:1  # Previous version
```

**Azure Container Apps:**
```bash
az containerapp revision list \
  --name fastttsr \
  --resource-group fastttsr-rg

az containerapp revision activate \
  --name fastttsr \
  --resource-group fastttsr-rg \
  --revision fastttsr--previous-revision
```

---

## Support and Maintenance

### Regular Maintenance Tasks

**Daily:**
- Check logs for errors
- Monitor resource usage
- Verify health checks

**Weekly:**
- Review performance metrics
- Check for security updates
- Analyze cost reports

**Monthly:**
- Update dependencies
- Review and rotate logs
- Capacity planning review
- Security audit

### Emergency Contacts

Create a runbook with:
- On-call rotation
- Escalation procedures
- Service dependencies
- Recovery procedures
- Contact information
