variable "environment" {
  description = "Ambiente: homolog ou prod"
  type        = string

  validation {
    condition     = contains(["homolog", "prod"], var.environment)
    error_message = "environment deve ser homolog ou prod."
  }
}

variable "region" {
  description = "Região AWS (AWS Academy usa us-east-1)"
  type        = string
  default     = "us-east-1"
}
