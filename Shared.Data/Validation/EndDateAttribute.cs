﻿using System.ComponentModel.DataAnnotations;

namespace VeloTimer.Shared.Data.Validation
{
    public class EndDateAttribute : ValidationAttribute
    {
        public string otherPropertyName;
        public EndDateAttribute() { }
        public EndDateAttribute(string otherPropertyName, string errorMessage) : base(errorMessage)
        {
            this.otherPropertyName = otherPropertyName;
        }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            ValidationResult validationResult = ValidationResult.Success;

            try
            {
                var containerType = validationContext.ObjectInstance.GetType();
                var field = containerType.GetProperty(otherPropertyName);
                var extensionValue = field.GetValue(validationContext.ObjectInstance, null);
                var dataType = extensionValue.GetType();

                if (field == null)
                {
                    return new ValidationResult($"Unknown property: {otherPropertyName}");
                }

                if (field.PropertyType == typeof(DateTime)
                    || field.PropertyType.IsGenericType && field.PropertyType == typeof(DateTime?))
                {
                    DateTime toValidate = (DateTime)value;
                    DateTime referenceProperty = (DateTime)field.GetValue(validationContext.ObjectInstance, null);

                    if (toValidate <= referenceProperty)
                    {
                        validationResult = new ValidationResult(ErrorMessageString);
                    }
                }
                else
                {
                    validationResult = new ValidationResult("An error occurred while validating the property. OtherProperty is not of type DateTime");
                }
            }
            catch (Exception)
            {
                throw;
            }

            return validationResult;
        }
    }
}
