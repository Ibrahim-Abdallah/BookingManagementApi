using BookingManagementApi.Enums;

namespace BookingManagementApi.Services.Reservations;

public static class ReservationStatusRules
{
    public static bool CanTransition(ReservationStatus current, ReservationStatus target) => (current, target) switch
    {
        (ReservationStatus.Held, ReservationStatus.Confirmed or ReservationStatus.Cancelled or ReservationStatus.Expired) => true,
        (ReservationStatus.Confirmed, ReservationStatus.Cancelled or ReservationStatus.Completed or ReservationStatus.NoShow) => true,
        _ => false
    };
}
